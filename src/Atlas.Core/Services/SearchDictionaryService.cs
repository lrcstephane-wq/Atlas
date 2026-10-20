using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed class SearchDictionaryStore(string sharedRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath => Path.Combine(sharedRoot, "Configuration", "search-dictionary.atlas.json");

    public async Task<SearchDictionaryDocument> LoadAsync(IEnumerable<SearchSynonymRecord>? legacy = null, CancellationToken cancellationToken = default)
    {
        SearchDictionaryDocument document;
        if (File.Exists(FilePath))
        {
            await using var stream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            document = await JsonSerializer.DeserializeAsync<SearchDictionaryDocument>(stream, JsonOptions, cancellationToken) ?? new();
        }
        else document = new();

        SearchDictionaryService.MergeOfficial(document, legacy);
        await SaveAsync(document, cancellationToken);
        return document;
    }

    public async Task SaveAsync(SearchDictionaryDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        document.SchemaVersion = Math.Max(1, document.SchemaVersion);
        var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        File.Move(temporary, FilePath, true);
    }
}

public static class SearchDictionaryService
{
    public const int OfficialVersion = 1;

    public static IReadOnlyList<SearchConceptRecord> OfficialConcepts { get; } = BuildOfficialConcepts();

    public static IReadOnlySet<string> IgnoredWords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "je", "cherche", "recherche", "veux", "voudrais", "souhaite", "faut", "me", "moi", "montre", "trouve",
        "avec", "ayant", "equipe", "equipee", "contenant", "mais", "que", "seulement", "uniquement", "un", "une", "des", "du", "de", "la", "le", "les",
        "d", "l", "a", "en", "dans", "dedans", "dessus", "au", "aux", "pour", "qui", "soit", "sont", "est", "et", "ou", "non",
        "different", "differents", "differente", "differentes", "meme", "memes", "meuble", "mobilier"
    };

    public static void MergeOfficial(SearchDictionaryDocument document, IEnumerable<SearchSynonymRecord>? legacy = null)
    {
        document.Concepts ??= [];
        document.UnknownQueries ??= [];
        foreach (var source in OfficialConcepts)
        {
            var existing = document.Concepts.FirstOrDefault(item => item.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                document.Concepts.Add(Clone(source));
                continue;
            }
            existing.Label = source.Label;
            existing.Category = source.Category;
            existing.Target = source.Target;
            existing.TargetValue = source.TargetValue;
            existing.OfficialAliasesCsv = source.OfficialAliasesCsv;
            existing.IsOfficial = true;
            existing.IsStrict = source.IsStrict;
        }

        foreach (var old in legacy ?? [])
        {
            if (string.IsNullOrWhiteSpace(old.Canonical) || CatalogSearchEngine.Normalize(old.Canonical) is "meuble" or "mobilier") continue;
            var match = document.Concepts.FirstOrDefault(item => item.Expressions().Any(expression =>
                CatalogSearchEngine.Normalize(expression).Equals(CatalogSearchEngine.Normalize(old.Canonical), StringComparison.Ordinal)));
            if (match is not null)
            {
                var canonical = CatalogSearchEngine.Normalize(old.Canonical);
                var knownDefaults = LegacyDefaultAliases.GetValueOrDefault(canonical) ?? new HashSet<string>(StringComparer.Ordinal);
                var additions = (old.AliasesCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(alias => !knownDefaults.Contains(CatalogSearchEngine.Normalize(alias)));
                match.CustomAliasesCsv = MergeAliases(match.CustomAliasesCsv, string.Join(", ", additions));
                continue;
            }
            var id = $"legacy-{old.Id}";
            if (document.Concepts.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            document.Concepts.Add(new SearchConceptRecord
            {
                Id = id,
                Label = old.Canonical.Trim(),
                Category = "Importé",
                Target = SearchConceptTarget.GenericText,
                CustomAliasesCsv = old.AliasesCsv,
                IsOfficial = false,
                IsStrict = false
            });
        }
        document.OfficialVersion = OfficialVersion;
    }

    public static SearchIntent Interpret(string query, IEnumerable<SearchConceptRecord> concepts, IEnumerable<string>? dynamicTags = null)
    {
        var active = concepts.Where(item => item.IsActive).ToList();
        foreach (var tag in dynamicTags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            active.Add(new SearchConceptRecord
            {
                Id = $"tag-{CatalogSearchEngine.Normalize(tag)}",
                Label = tag,
                Category = "Tags",
                Target = SearchConceptTarget.FurnitureTag,
                TargetValue = tag,
                IsStrict = true
            });
        }

        var normalized = CatalogSearchEngine.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalized)) return new SearchIntent { OriginalQuery = query };
        var rawTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var vocabulary = active.SelectMany(item => item.Expressions()).SelectMany(expression => CatalogSearchEngine.Normalize(expression).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Concat(IgnoredWords).Concat(NumberWords.Keys).Concat(["sans", "minimum", "maximum", "max", "exactement", "moins", "plus"])
            .Distinct(StringComparer.Ordinal).ToArray();
        var correctedTokens = rawTokens.Select(token => CorrectToken(token, vocabulary)).ToArray();
        var occupied = new bool[correctedTokens.Length];
        var strictOccupied = new bool[correctedTokens.Length];
        var constraints = new List<SearchConstraint>();
        var recognized = new List<string>();

        var expressions = active.SelectMany(concept => concept.Expressions()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => (Concept: concept, Tokens: CatalogSearchEngine.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))))
            .OrderByDescending(item => item.Tokens.Length).ToArray();
        foreach (var (concept, expressionTokens) in expressions)
        {
            for (var index = 0; index <= correctedTokens.Length - expressionTokens.Length; index++)
            {
                if (!MatchesAt(correctedTokens, expressionTokens, index)) continue;
                if (concept.IsStrict && expressionTokens.Length == 1 && strictOccupied[index]) continue;
                for (var tokenIndex = index; tokenIndex < index + expressionTokens.Length; tokenIndex++)
                {
                    occupied[tokenIndex] = true;
                    if (concept.IsStrict) strictOccupied[tokenIndex] = true;
                }
                var constraint = BuildConstraint(concept, correctedTokens, index);
                if (!constraints.Any(item => SameConstraint(item, constraint))) constraints.Add(constraint);
                if (!recognized.Contains(concept.Label, StringComparer.OrdinalIgnoreCase)) recognized.Add(concept.Label);
            }
        }

        for (var index = 0; index < correctedTokens.Length; index++)
        {
            if (IgnoredWords.Contains(correctedTokens[index]) || IsControlToken(correctedTokens[index]) || ParseNumber(correctedTokens[index]) is not null)
                occupied[index] = true;
        }
        var remaining = string.Join(' ', correctedTokens.Where((_, index) => !occupied[index]));
        return new SearchIntent
        {
            OriginalQuery = query,
            CorrectedQuery = string.Join(' ', correctedTokens),
            RemainingQuery = remaining,
            Constraints = constraints,
            RecognizedExpressions = recognized,
            Summary = BuildSummary(constraints, remaining)
        };
    }

    private static SearchConstraint BuildConstraint(SearchConceptRecord concept, string[] tokens, int expressionStart)
    {
        var mode = SearchConstraintMode.Contains;
        var quantity = 0;
        if (IsCountTarget(concept.Target))
        {
            mode = SearchConstraintMode.Minimum;
            quantity = 1;
            var numberIndex = expressionStart - 1;
            if (numberIndex >= 0 && ParseNumber(tokens[numberIndex]) is { } parsed)
            {
                quantity = parsed;
                mode = SearchConstraintMode.Exact;
                if (numberIndex >= 2 && tokens[numberIndex - 2] == "au" && tokens[numberIndex - 1] == "moins") mode = SearchConstraintMode.Minimum;
                else if (numberIndex >= 1 && tokens[numberIndex - 1] is "minimum" or "mini") mode = SearchConstraintMode.Minimum;
                else if (numberIndex >= 1 && tokens[numberIndex - 1] is "maximum" or "max") mode = SearchConstraintMode.Maximum;
                else if (numberIndex >= 1 && tokens[numberIndex - 1] == "exactement") mode = SearchConstraintMode.Exact;
            }
            if (tokens.Skip(Math.Max(0, expressionStart - 2)).Take(Math.Min(2, expressionStart)).Contains("sans"))
            {
                mode = SearchConstraintMode.Exact;
                quantity = 0;
            }
        }
        return new SearchConstraint(concept.Target, string.IsNullOrWhiteSpace(concept.TargetValue) ? concept.Label : concept.TargetValue,
            mode, quantity, concept.IsStrict, concept.Label);
    }

    private static bool SameConstraint(SearchConstraint left, SearchConstraint right) =>
        left.Target == right.Target && left.Value.Equals(right.Value, StringComparison.OrdinalIgnoreCase) && left.Mode == right.Mode && left.Quantity == right.Quantity;

    private static string BuildSummary(IEnumerable<SearchConstraint> constraints, string remaining)
    {
        var parts = constraints.Select(constraint => constraint.Target switch
        {
            SearchConceptTarget.DoorCount => QuantitySummary("porte", constraint),
            SearchConceptTarget.DrawerCount => QuantitySummary("tiroir", constraint),
            SearchConceptTarget.InnerDrawerCount => QuantitySummary("tiroir à l’anglaise", constraint),
            SearchConceptTarget.FlapCount => QuantitySummary("abattant", constraint),
            SearchConceptTarget.LiftUpCount => QuantitySummary("relevant", constraint),
            SearchConceptTarget.ShelfCount => QuantitySummary("rayon", constraint),
            SearchConceptTarget.OpenNicheCount => QuantitySummary("niche ouverte", constraint),
            SearchConceptTarget.FurnitureTag => $"tag {constraint.Value}",
            SearchConceptTarget.FurnitureUsage => $"usage {constraint.Value}",
            SearchConceptTarget.FurnitureType => $"préférence {constraint.Value}",
            _ => constraint.ConceptLabel
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(remaining)) parts.Add($"texte « {remaining} »");
        return parts.Count == 0 ? "Aucun critère métier reconnu" : string.Join(" · ", parts);
    }

    private static string QuantitySummary(string singular, SearchConstraint constraint)
    {
        var plural = constraint.Quantity > 1 ? singular + "s" : singular;
        return constraint.Mode switch
        {
            SearchConstraintMode.Exact => $"{constraint.Quantity} {plural}",
            SearchConstraintMode.Maximum => $"maximum {constraint.Quantity} {plural}",
            _ => constraint.Quantity <= 1 ? $"au moins 1 {singular}" : $"au moins {constraint.Quantity} {plural}"
        };
    }

    public static bool QuantityMatches(int actual, SearchConstraint constraint) => constraint.Mode switch
    {
        SearchConstraintMode.Exact => actual == constraint.Quantity,
        SearchConstraintMode.Maximum => actual <= constraint.Quantity,
        SearchConstraintMode.Minimum => actual >= constraint.Quantity,
        _ => actual > 0
    };

    private static bool IsCountTarget(SearchConceptTarget target) => target is SearchConceptTarget.DoorCount or SearchConceptTarget.DrawerCount
        or SearchConceptTarget.InnerDrawerCount or SearchConceptTarget.FlapCount or SearchConceptTarget.LiftUpCount
        or SearchConceptTarget.ShelfCount or SearchConceptTarget.OpenNicheCount;

    private static bool IsControlToken(string token) => token is "sans" or "minimum" or "mini" or "maximum" or "max" or "exactement" or "moins" or "plus";

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5,
        ["six"] = 6, ["sept"] = 7, ["huit"] = 8, ["neuf"] = 9, ["dix"] = 10, ["onze"] = 11, ["douze"] = 12
    };

    private static readonly Dictionary<string, HashSet<string>> LegacyDefaultAliases = new(StringComparer.Ordinal)
    {
        ["sous evier"] = ["evier", "lavabo"],
        ["tiroir"] = ["coulissant", "rangement"],
        ["porte"] = ["facade", "ouvrant"],
        ["charniere"] = ["facade battante", "porte battante"],
        ["refrigerateur"] = ["frigo"],
        ["poubelle"] = ["dechets", "tri selectif"],
        ["four"] = ["cuisson"]
    };

    private static int? ParseNumber(string token)
    {
        if (int.TryParse(token, out var number)) return number;
        return NumberWords.TryGetValue(token, out var word) ? word : null;
    }

    private static bool MatchesAt(string[] source, string[] expected, int start)
    {
        for (var index = 0; index < expected.Length; index++)
            if (!source[start + index].Equals(expected[index], StringComparison.Ordinal)) return false;
        return true;
    }

    private static string CorrectToken(string token, IReadOnlyCollection<string> vocabulary)
    {
        if (token.Length <= 2 || int.TryParse(token, out _) || vocabulary.Contains(token, StringComparer.Ordinal)) return token;
        var best = vocabulary.Select(candidate => (Candidate: candidate, Score: Similarity(token, candidate)))
            .OrderByDescending(item => item.Score).ThenBy(item => item.Candidate.Length).FirstOrDefault();
        var threshold = token.Length <= 5 ? 0.74d : token.Length <= 8 ? 0.66d : 0.62d;
        return best.Score >= threshold ? best.Candidate : token;
    }

    private static double Similarity(string left, string right)
    {
        if (left == right) return 1d;
        var matrix = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++) matrix[i, 0] = i;
        for (var j = 0; j <= right.Length; j++) matrix[0, j] = j;
        for (var i = 1; i <= left.Length; i++)
        for (var j = 1; j <= right.Length; j++)
        {
            var cost = left[i - 1] == right[j - 1] ? 0 : 1;
            matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1), matrix[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && left[i - 1] == right[j - 2] && left[i - 2] == right[j - 1])
                matrix[i, j] = Math.Min(matrix[i, j], matrix[i - 2, j - 2] + cost);
        }
        return 1d - (double)matrix[left.Length, right.Length] / Math.Max(left.Length, right.Length);
    }

    private static SearchConceptRecord Clone(SearchConceptRecord source) => new()
    {
        Id = source.Id, Label = source.Label, Category = source.Category, Target = source.Target, TargetValue = source.TargetValue,
        OfficialAliasesCsv = source.OfficialAliasesCsv, CustomAliasesCsv = source.CustomAliasesCsv, IsOfficial = source.IsOfficial,
        IsActive = source.IsActive, IsStrict = source.IsStrict
    };

    private static string MergeAliases(params string[] values) => string.Join(", ", values.SelectMany(value =>
            (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<SearchConceptRecord> BuildOfficialConcepts() =>
    [
        Official("element-porte", "Porte", "Éléments", SearchConceptTarget.DoorCount, "", "ouvrant, porte battante"),
        Official("element-tiroir", "Tiroir", "Éléments", SearchConceptTarget.DrawerCount, "", "coulissant de rangement"),
        Official("element-tiroir-anglais", "Tiroir à l’anglaise", "Éléments", SearchConceptTarget.InnerDrawerCount, "", "tiroir anglais, tiroir intérieur, coulissant intérieur"),
        Official("element-rayon", "Rayon", "Éléments", SearchConceptTarget.ShelfCount, "", "tablette, étagère"),
        Official("expression-rangement-rayons", "Armoire de rangement", "Expressions", SearchConceptTarget.ShelfCount, "", "placard de rangement, rangement avec étagères"),
        Official("element-penderie", "Penderie", "Éléments", SearchConceptTarget.Hanging, "", "tringle, barre de penderie"),
        Official("element-relevant", "Relevant", "Éléments", SearchConceptTarget.LiftUpCount, "", "hayon, porte relevable, ouvrant vers le haut"),
        Official("element-abattant", "Abattant", "Éléments", SearchConceptTarget.FlapCount, "", "porte abattante, ouvrant vers le bas"),
        Official("element-niche", "Niche ouverte", "Éléments", SearchConceptTarget.OpenNicheCount, "", "niche, case ouverte, compartiment ouvert"),
        Official("element-separation-h", "Séparation horizontale", "Éléments", SearchConceptTarget.HorizontalSeparation, "", "cloison horizontale"),
        Official("element-separation-v", "Séparation verticale", "Éléments", SearchConceptTarget.VerticalSeparation, "", "cloison verticale"),

        Official("usage-sous-evier", "Sous-évier", "Usages", SearchConceptTarget.FurnitureUsage, "Sous-évier", "sous évier, meuble évier, caisson évier"),
        Official("usage-vasque", "Vasque", "Usages", SearchConceptTarget.FurnitureUsage, "Vasque", "meuble vasque, sous vasque, lavabo"),
        Official("usage-four", "Four", "Usages", SearchConceptTarget.FurnitureUsage, "Four", "meuble four, colonne four"),
        Official("usage-micro-ondes", "Micro-ondes", "Usages", SearchConceptTarget.FurnitureUsage, "Micro-ondes", "micro onde, meuble micro onde"),
        Official("usage-refrigerateur", "Réfrigérateur", "Usages", SearchConceptTarget.FurnitureUsage, "Réfrigérateur", "frigo, meuble frigo"),
        Official("usage-lave-vaisselle", "Lave-vaisselle", "Usages", SearchConceptTarget.FurnitureUsage, "Lave-vaisselle", "lave vaisselle, lv"),
        Official("usage-lave-linge", "Lave-linge", "Usages", SearchConceptTarget.FurnitureUsage, "Lave-linge", "lave linge, machine à laver"),
        Official("usage-seche-linge", "Sèche-linge", "Usages", SearchConceptTarget.FurnitureUsage, "Sèche-linge", "sèche linge"),
        Official("usage-poubelle", "Poubelle", "Usages", SearchConceptTarget.FurnitureUsage, "Poubelle", "tri, tri sélectif, déchets"),
        Official("usage-chaussures", "Chaussures", "Usages", SearchConceptTarget.FurnitureUsage, "Chaussures", "meuble à chaussures"),
        Official("usage-tv", "TV / multimédia", "Usages", SearchConceptTarget.FurnitureUsage, "TV / multimédia", "télévision, télé, tv, multimédia"),
        Official("usage-technique", "Technique", "Usages", SearchConceptTarget.FurnitureUsage, "Technique", "meuble technique, coffret technique"),

        Official("type-bas", "Meuble bas", "Types", SearchConceptTarget.FurnitureType, "Meuble bas", "caisson bas, élément bas", false),
        Official("type-haut", "Meuble haut", "Types", SearchConceptTarget.FurnitureType, "Meuble haut", "élément haut, meuble suspendu", false),
        Official("type-colonne", "Colonne", "Types", SearchConceptTarget.FurnitureType, "Colonne", "armoire, tour, meuble vertical", false),
        Official("type-placard", "Placard", "Types", SearchConceptTarget.FurnitureType, "Armoire", "rangement fermé", false),

        Official("forme-angle", "Angle", "Formes", SearchConceptTarget.FurnitureForm, "Angle", "meuble d’angle, coin"),
        Official("forme-sous-rampant", "Sous rampant", "Formes", SearchConceptTarget.FurnitureForm, "Sous rampant", "sous-rampant, mansarde, combles"),
        Official("forme-courbe", "Courbe", "Formes", SearchConceptTarget.FurnitureForm, "Courbe", "cintré, arrondi"),
        Official("forme-pan-coupe", "Pan coupé", "Formes", SearchConceptTarget.FurnitureForm, "Pan coupé", "biais"),

        Official("construction-montant", "Montant filant", "Construction", SearchConceptTarget.Construction, "Montant filant", "côtés filants, côté filant"),
        Official("construction-traverse", "Traverse filante", "Construction", SearchConceptTarget.Construction, "Traverse filante", "dessus dessous filants"),
        Official("dos-sans", "Sans dos", "Construction", SearchConceptTarget.BackPosition, "Sans dos", "ouvert derrière"),
        Official("dos-applique", "Dos appliqué", "Construction", SearchConceptTarget.BackPosition, "Appliqué", "dos en applique"),
        Official("dos-rainure", "Dos rainuré", "Construction", SearchConceptTarget.BackPosition, "Rainuré", "dos en rainure"),
        Official("dos-interieur", "Dos intérieur", "Construction", SearchConceptTarget.BackPosition, "Intérieur", "dos dedans"),
        Official("assemblage-vis", "Vis", "Assemblages", SearchConceptTarget.Assembly, "Vis", "vissé, assemblage vissé"),
        Official("assemblage-tourillons", "Tourillons", "Assemblages", SearchConceptTarget.Assembly, "Tourillons", "tourillonné"),
        Official("assemblage-tourillons-vis", "Tourillons + vis", "Assemblages", SearchConceptTarget.Assembly, "Tourillons + vis", "tourillon vis"),
        Official("assemblage-excentrique", "Excentrique", "Assemblages", SearchConceptTarget.Assembly, "Excentrique", "excentriques"),
        Official("assemblage-clamex", "Clamex", "Assemblages", SearchConceptTarget.Assembly, "Clamex", "lamello clamex"),
        Official("assemblage-cabineo", "Cabineo", "Assemblages", SearchConceptTarget.Assembly, "Cabineo", "lamello cabineo")
    ];

    private static SearchConceptRecord Official(string id, string label, string category, SearchConceptTarget target, string value, string aliases, bool strict = true) => new()
    {
        Id = id, Label = label, Category = category, Target = target, TargetValue = value, OfficialAliasesCsv = aliases,
        IsOfficial = true, IsActive = true, IsStrict = strict
    };
}
