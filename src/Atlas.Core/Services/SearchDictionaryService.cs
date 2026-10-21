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
    public const int OfficialVersion = 2;

    public static IReadOnlyList<SearchConceptRecord> OfficialConcepts { get; } = BuildOfficialConcepts();
    public static IReadOnlyList<SearchSynonymEntry> OfficialSynonyms { get; } = BuildOfficialSynonyms();
    public static IReadOnlyList<SearchExpressionRecord> OfficialExpressions { get; } = BuildOfficialExpressions();
    public static IReadOnlyList<SearchIgnoredTermRecord> OfficialIgnoredTerms { get; } = BuildOfficialIgnoredTerms();

    public static void MergeOfficial(SearchDictionaryDocument document, IEnumerable<SearchSynonymRecord>? legacy = null)
    {
        document.Concepts ??= [];
        document.Synonyms ??= [];
        document.Expressions ??= [];
        document.IgnoredTerms ??= [];
        document.UnknownQueries ??= [];

        // Migration transparente du schéma v1 : les ajouts saisis par le client deviennent de vraies lignes de synonymes.
        foreach (var concept in document.Concepts)
        foreach (var alias in SplitAliases(concept.CustomAliasesCsv))
            AddIfMissing(document.Synonyms, new SearchSynonymEntry
            {
                Id = $"migrated-{concept.Id}-{StableId(alias)}", ConceptId = concept.Id, Term = alias,
                IsOfficial = false, IsActive = true
            });

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

        MergeOfficialRows(document.Synonyms, OfficialSynonyms, item => item.Id, Clone);
        MergeOfficialRows(document.Expressions, OfficialExpressions, item => item.Id, Clone);
        MergeOfficialRows(document.IgnoredTerms, OfficialIgnoredTerms, item => item.Id, Clone);

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
                CustomAliasesCsv = old.AliasesCsv ?? string.Empty,
                IsOfficial = false,
                IsStrict = false
            });
        }
        document.SchemaVersion = 2;
        document.OfficialVersion = OfficialVersion;
    }

    public static SearchIntent Interpret(string query, IEnumerable<SearchConceptRecord> concepts, IEnumerable<string>? dynamicTags = null)
    {
        var document = new SearchDictionaryDocument { Concepts = concepts.ToList() };
        document.Synonyms.AddRange(OfficialSynonyms.Select(Clone));
        document.Expressions.AddRange(OfficialExpressions.Select(Clone));
        document.IgnoredTerms.AddRange(OfficialIgnoredTerms.Select(Clone));
        return Interpret(query, document, dynamicTags);
    }

    public static SearchIntent Interpret(string query, SearchDictionaryDocument document, IEnumerable<string>? dynamicTags = null)
    {
        var active = (document.Concepts ?? []).Where(item => item.IsActive).ToList();
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
        var synonyms = (document.Synonyms ?? []).Where(item => item.IsActive).ToList();
        var expressionRules = (document.Expressions ?? []).Where(item => item.IsActive).ToList();
        var ignoredRules = (document.IgnoredTerms ?? []).Where(item => item.IsActive).ToList();
        var vocabulary = active.SelectMany(item => new[] { item.Label })
            .Concat(synonyms.Select(item => item.Term)).Concat(expressionRules.Select(item => item.Phrase))
            .Concat(ignoredRules.Select(item => item.Term))
            .SelectMany(expression => CatalogSearchEngine.Normalize(expression).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Concat(NumberWords.Keys).Concat(["sans", "pas", "aucun", "aucune", "minimum", "mini", "maximum", "max", "exactement", "moins", "plus"])
            .Distinct(StringComparer.Ordinal).ToArray();
        var correctedTokens = rawTokens.Select(token => CorrectToken(token, vocabulary)).ToArray();
        var occupied = new bool[correctedTokens.Length];
        var strictOccupied = new bool[correctedTokens.Length];
        var constraints = new List<SearchConstraint>();
        var recognized = new List<string>();
        var recognizedIds = new List<string>();
        var ignored = new List<string>();

        // Les expressions composées sont prioritaires et peuvent déclencher plusieurs concepts.
        var phrases = expressionRules.Where(item => !string.IsNullOrWhiteSpace(item.Phrase))
            .Select(item => (Rule: item, Tokens: CatalogSearchEngine.Normalize(item.Phrase).Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .OrderByDescending(item => item.Tokens.Length).ToArray();
        foreach (var (rule, expressionTokens) in phrases)
        {
            for (var index = 0; index <= correctedTokens.Length - expressionTokens.Length; index++)
            {
                if (!MatchesAt(correctedTokens, expressionTokens, index)) continue;
                if (Enumerable.Range(index, expressionTokens.Length).Any(tokenIndex => strictOccupied[tokenIndex])) continue;
                for (var tokenIndex = index; tokenIndex < index + expressionTokens.Length; tokenIndex++)
                {
                    occupied[tokenIndex] = true;
                    strictOccupied[tokenIndex] = true;
                }
                foreach (var conceptId in rule.ConceptIds())
                {
                    var concept = active.FirstOrDefault(item => item.Id.Equals(conceptId, StringComparison.OrdinalIgnoreCase));
                    if (concept is null) continue;
                    AddConceptMatch(concept, correctedTokens, index, constraints, recognizedIds);
                }
                if (!recognized.Contains(rule.Phrase, StringComparer.OrdinalIgnoreCase)) recognized.Add(rule.Phrase);
            }
        }

        var terms = active.Select(concept => (Concept: concept, Value: concept.Label))
            .Concat(synonyms.Select(item => (Concept: active.FirstOrDefault(concept => concept.Id.Equals(item.ConceptId, StringComparison.OrdinalIgnoreCase)), Value: item.Term))
                .Where(item => item.Concept is not null).Select(item => (Concept: item.Concept!, Value: item.Value)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => (item.Concept, Tokens: CatalogSearchEngine.Normalize(item.Value).Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .OrderByDescending(item => item.Tokens.Length).ToArray();
        foreach (var (concept, termTokens) in terms)
        for (var index = 0; index <= correctedTokens.Length - termTokens.Length; index++)
        {
            if (!MatchesAt(correctedTokens, termTokens, index)) continue;
            if (Enumerable.Range(index, termTokens.Length).Any(tokenIndex => strictOccupied[tokenIndex])) continue;
            for (var tokenIndex = index; tokenIndex < index + termTokens.Length; tokenIndex++)
            {
                occupied[tokenIndex] = true;
                if (concept.IsStrict) strictOccupied[tokenIndex] = true;
            }
            AddConceptMatch(concept, correctedTokens, index, constraints, recognizedIds);
            if (!recognized.Contains(concept.Label, StringComparer.OrdinalIgnoreCase)) recognized.Add(concept.Label);
        }

        foreach (var rule in ignoredRules.OrderByDescending(item => item.Term.Length))
        {
            var termTokens = CatalogSearchEngine.Normalize(rule.Term).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index <= correctedTokens.Length - termTokens.Length; index++)
            {
                if (!MatchesAt(correctedTokens, termTokens, index)) continue;
                for (var tokenIndex = index; tokenIndex < index + termTokens.Length; tokenIndex++) occupied[tokenIndex] = true;
                if (!ignored.Contains(rule.Term, StringComparer.OrdinalIgnoreCase)) ignored.Add(rule.Term);
            }
        }
        for (var index = 0; index < correctedTokens.Length; index++)
            if (IsControlToken(correctedTokens[index]) || ParseNumber(correctedTokens[index]) is not null) occupied[index] = true;
        var remaining = string.Join(' ', correctedTokens.Where((_, index) => !occupied[index]));
        return new SearchIntent
        {
            OriginalQuery = query,
            CorrectedQuery = string.Join(' ', correctedTokens),
            RemainingQuery = remaining,
            Constraints = constraints,
            RecognizedExpressions = recognized,
            RecognizedConceptIds = recognizedIds,
            IgnoredTerms = ignored,
            Summary = BuildSummary(constraints, remaining)
        };
    }

    private static void AddConceptMatch(SearchConceptRecord concept, string[] tokens, int index,
        ICollection<SearchConstraint> constraints, ICollection<string> recognizedIds)
    {
        var constraint = BuildConstraint(concept, tokens, index);
        if (!constraints.Any(item => SameConstraint(item, constraint))) constraints.Add(constraint);
        if (!recognizedIds.Contains(concept.Id, StringComparer.OrdinalIgnoreCase)) recognizedIds.Add(concept.Id);
    }

    private static SearchConstraint BuildConstraint(SearchConceptRecord concept, string[] tokens, int expressionStart)
    {
        var mode = SearchConstraintMode.Contains;
        var quantity = 0;
        var previous = tokens.Skip(Math.Max(0, expressionStart - 3)).Take(Math.Min(3, expressionStart)).ToArray();
        var excluded = previous.Contains("sans") || previous.Contains("aucun") || previous.Contains("aucune") ||
                       previous.Contains("pas");
        if (IsQuantityTarget(concept.Target))
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
            if (excluded)
            {
                mode = SearchConstraintMode.Exact;
                quantity = 0;
            }
        }
        else if (excluded) mode = SearchConstraintMode.Exclude;
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
            SearchConceptTarget.FurnitureTag => constraint.Mode == SearchConstraintMode.Exclude ? $"sans tag {constraint.Value}" : $"tag {constraint.Value}",
            SearchConceptTarget.FurnitureUsage => constraint.Mode == SearchConstraintMode.Exclude ? $"sans usage {constraint.Value}" : $"usage {constraint.Value}",
            SearchConceptTarget.FurnitureType => $"préférence {constraint.Value}",
            _ => constraint.Mode == SearchConstraintMode.Exclude ? $"sans {constraint.ConceptLabel}" : constraint.ConceptLabel
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
            SearchConstraintMode.Exclude => $"sans {singular}",
            _ => constraint.Quantity <= 1 ? $"au moins 1 {singular}" : $"au moins {constraint.Quantity} {plural}"
        };
    }

    public static bool QuantityMatches(int actual, SearchConstraint constraint) => constraint.Mode switch
    {
        SearchConstraintMode.Exact => actual == constraint.Quantity,
        SearchConstraintMode.Maximum => actual <= constraint.Quantity,
        SearchConstraintMode.Minimum => actual >= constraint.Quantity,
        SearchConstraintMode.Exclude => actual == 0,
        _ => actual > 0
    };

    public static bool IsQuantityTarget(SearchConceptTarget target) => target is SearchConceptTarget.DoorCount or SearchConceptTarget.DrawerCount
        or SearchConceptTarget.InnerDrawerCount or SearchConceptTarget.FlapCount or SearchConceptTarget.LiftUpCount
        or SearchConceptTarget.ShelfCount or SearchConceptTarget.OpenNicheCount;

    private static bool IsControlToken(string token) => token is "sans" or "pas" or "aucun" or "aucune" or "minimum" or "mini" or
        "maximum" or "max" or "exactement" or "moins" or "plus" or "avec" or "mais" or "que" or "seulement" or "uniquement";

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

    private static SearchSynonymEntry Clone(SearchSynonymEntry source) => new()
    {
        Id = source.Id, ConceptId = source.ConceptId, Term = source.Term, IsOfficial = source.IsOfficial, IsActive = source.IsActive
    };

    private static SearchExpressionRecord Clone(SearchExpressionRecord source) => new()
    {
        Id = source.Id, Phrase = source.Phrase, ConceptIdsCsv = source.ConceptIdsCsv, Description = source.Description,
        IsOfficial = source.IsOfficial, IsActive = source.IsActive
    };

    private static SearchIgnoredTermRecord Clone(SearchIgnoredTermRecord source) => new()
    {
        Id = source.Id, Term = source.Term, IsOfficial = source.IsOfficial, IsActive = source.IsActive
    };

    private static void MergeOfficialRows<T>(ICollection<T> destination, IEnumerable<T> sources,
        Func<T, string> id, Func<T, T> clone)
    {
        foreach (var source in sources)
        {
            var existing = destination.FirstOrDefault(item => id(item).Equals(id(source), StringComparison.OrdinalIgnoreCase));
            if (existing is null) destination.Add(clone(source));
            else
            {
                var active = existing switch
                {
                    SearchSynonymEntry value => value.IsActive,
                    SearchExpressionRecord value => value.IsActive,
                    SearchIgnoredTermRecord value => value.IsActive,
                    _ => true
                };
                destination.Remove(existing);
                var replacement = clone(source);
                switch (replacement)
                {
                    case SearchSynonymEntry value: value.IsActive = active; break;
                    case SearchExpressionRecord value: value.IsActive = active; break;
                    case SearchIgnoredTermRecord value: value.IsActive = active; break;
                }
                destination.Add(replacement);
            }
        }
    }

    private static void AddIfMissing(ICollection<SearchSynonymEntry> destination, SearchSynonymEntry source)
    {
        var normalized = CatalogSearchEngine.Normalize(source.Term);
        if (destination.Any(item => item.ConceptId.Equals(source.ConceptId, StringComparison.OrdinalIgnoreCase) &&
                                    CatalogSearchEngine.Normalize(item.Term).Equals(normalized, StringComparison.Ordinal))) return;
        destination.Add(source);
    }

    private static IEnumerable<string> SplitAliases(string value) => (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string StableId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(CatalogSearchEngine.Normalize(value)));
        return Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }

    private static string MergeAliases(params string[] values) => string.Join(", ", values.SelectMany(value =>
            (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<SearchConceptRecord> BuildOfficialConcepts() =>
    [
        Official("element-porte", "Porte", "Éléments", SearchConceptTarget.DoorCount, "", "ouvrant, vantail, porte battante"),
        Official("element-tiroir", "Tiroir", "Éléments", SearchConceptTarget.DrawerCount, "", "coulissant de rangement, tiroir simple"),
        Official("element-tiroir-anglais", "Tiroir à l’anglaise", "Éléments", SearchConceptTarget.InnerDrawerCount, "", "tiroir anglais, tiroir intérieur, coulissant intérieur"),
        Official("element-rayon", "Rayon", "Éléments", SearchConceptTarget.ShelfCount, "", "tablette, étagère, planche de rangement"),
        Official("expression-rangement-rayons", "Armoire de rangement", "Expressions", SearchConceptTarget.ShelfCount, "", "placard de rangement, rangement avec étagères"),
        Official("element-penderie", "Penderie", "Éléments", SearchConceptTarget.Hanging, "", "tringle, barre de penderie"),
        Official("element-relevant", "Relevant", "Éléments", SearchConceptTarget.LiftUpCount, "", "hayon, porte relevable, ouvrant vers le haut"),
        Official("element-abattant", "Abattant", "Éléments", SearchConceptTarget.FlapCount, "", "porte abattante, ouvrant vers le bas"),
        Official("element-niche", "Niche ouverte", "Éléments", SearchConceptTarget.OpenNicheCount, "", "niche, case ouverte, compartiment ouvert"),
        Official("element-separation-h", "Séparation horizontale", "Éléments", SearchConceptTarget.HorizontalSeparation, "", "cloison horizontale"),
        Official("element-separation-v", "Séparation verticale", "Éléments", SearchConceptTarget.VerticalSeparation, "", "cloison verticale"),
        Official("element-tablette-reglable", "Tablette réglable", "Éléments", SearchConceptTarget.GenericText, "tablette réglable", "tablette mobile, rayon réglable, étagère réglable, tablette amovible"),
        Official("element-tablette-fixe", "Tablette fixe", "Éléments", SearchConceptTarget.GenericText, "tablette fixe", "rayon fixe, étagère fixe"),
        Official("element-panier-coulissant", "Panier coulissant", "Éléments", SearchConceptTarget.GenericText, "panier coulissant", "panier extractible, corbeille coulissante"),
        Official("element-plateau-tournant", "Plateau tournant", "Éléments", SearchConceptTarget.GenericText, "plateau tournant", "carrousel, tourniquet"),
        Official("element-porte-coulissante", "Porte coulissante", "Éléments", SearchConceptTarget.GenericText, "porte coulissante", "façade coulissante, ouvrant coulissant"),
        Official("element-porte-pliante", "Porte pliante", "Éléments", SearchConceptTarget.GenericText, "porte pliante", "porte accordéon, porte repliable"),
        Official("element-porte-escamotable", "Porte escamotable", "Éléments", SearchConceptTarget.GenericText, "porte escamotable", "porte rentrante, porte pocket, façade escamotable"),
        Official("element-tringle-basculante", "Tringle basculante", "Éléments", SearchConceptTarget.GenericText, "tringle basculante", "penderie escamotable, penderie descendante, lift penderie"),
        Official("element-porte-pantalons", "Porte-pantalons", "Éléments", SearchConceptTarget.GenericText, "porte-pantalons", "range pantalons, penderie pantalons"),
        Official("position-dessus", "Au-dessus", "Positions", SearchConceptTarget.SpatialRelation, "au-dessus", "", false),
        Official("position-dessous", "En dessous", "Positions", SearchConceptTarget.SpatialRelation, "en dessous", "", false),
        Official("position-haut", "En partie haute", "Positions", SearchConceptTarget.SpatialRelation, "en partie haute", "", false),
        Official("position-bas", "En partie basse", "Positions", SearchConceptTarget.SpatialRelation, "en partie basse", "", false),
        Official("position-gauche", "À gauche", "Positions", SearchConceptTarget.SpatialRelation, "à gauche", "", false),
        Official("position-droite", "À droite", "Positions", SearchConceptTarget.SpatialRelation, "à droite", "", false),

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
        Official("usage-imprimante", "Imprimante", "Usages", SearchConceptTarget.FurnitureUsage, "Imprimante", "meuble imprimante"),
        Official("usage-caisse", "Caisse", "Usages", SearchConceptTarget.FurnitureUsage, "Caisse", "meuble caisse, comptoir caisse"),
        Official("usage-presentation", "Présentation / exposition", "Usages", SearchConceptTarget.FurnitureUsage, "Présentation / exposition", "exposition, présentoir"),
        Official("usage-hotte", "Hotte", "Usages", SearchConceptTarget.GenericText, "hotte", "groupe filtrant, hotte intégrée, hotte encastrée"),
        Official("usage-plaque", "Plaque de cuisson", "Usages", SearchConceptTarget.GenericText, "plaque de cuisson", "table de cuisson, induction"),
        Official("usage-casserolier", "Casserolier", "Usages", SearchConceptTarget.GenericText, "casserolier", "tiroir casserolier, tiroir à casseroles"),
        Official("usage-epices", "Épices", "Usages", SearchConceptTarget.GenericText, "épices", "range épices, meuble à épices, épicier"),
        Official("usage-bouteilles", "Bouteilles", "Usages", SearchConceptTarget.GenericText, "bouteilles", "porte bouteilles, range bouteilles"),
        Official("usage-linge", "Linge", "Usages", SearchConceptTarget.GenericText, "linge", "lingère, rangement linge, colonne à linge"),
        Official("usage-entretien", "Entretien", "Usages", SearchConceptTarget.GenericText, "entretien", "placard balai, meuble à balais, rangement aspirateur"),
        Official("usage-dossiers", "Dossiers suspendus", "Usages", SearchConceptTarget.GenericText, "dossiers suspendus", "classement suspendu"),
        Official("usage-minibar", "Minibar", "Usages", SearchConceptTarget.GenericText, "minibar", "mini bar"),

        Official("type-bas", "Meuble bas", "Types", SearchConceptTarget.FurnitureType, "Meuble bas", "caisson bas, élément bas", false),
        Official("type-haut", "Meuble haut", "Types", SearchConceptTarget.FurnitureType, "Meuble haut", "élément haut, meuble suspendu", false),
        Official("type-colonne", "Colonne", "Types", SearchConceptTarget.FurnitureType, "Colonne", "tour, meuble vertical, colonne réserve", false),
        Official("type-demi-colonne", "Demi-colonne", "Types", SearchConceptTarget.FurnitureType, "Demi-colonne", "demi colonne", false),
        Official("type-armoire", "Armoire", "Types", SearchConceptTarget.FurnitureType, "Armoire", "armoire de rangement", false),
        Official("type-placard", "Placard", "Types", SearchConceptTarget.FurnitureType, "Armoire", "rangement fermé", false),
        Official("type-niche", "Meuble niche", "Types", SearchConceptTarget.FurnitureType, "Niche", "meuble ouvert", false),
        Official("type-banc", "Banc", "Types", SearchConceptTarget.FurnitureType, "Banc", "banquette", false),
        Official("type-bureau", "Bureau", "Types", SearchConceptTarget.FurnitureType, "Bureau", "poste de travail", false),
        Official("type-console", "Console", "Types", SearchConceptTarget.FurnitureType, "Console", "meuble console", false),
        Official("type-comptoir", "Comptoir", "Types", SearchConceptTarget.FurnitureType, "Comptoir", "banque d’accueil, banque accueil", false),
        Official("type-presentoir", "Présentoir", "Types", SearchConceptTarget.FurnitureType, "Présentoir", "gondole, meuble d’exposition", false),
        Official("type-bibliotheque", "Bibliothèque", "Types", SearchConceptTarget.GenericText, "bibliothèque", "biblio, rayonnage", false),
        Official("type-dressing", "Dressing", "Types", SearchConceptTarget.GenericText, "dressing", "placard dressing", false),
        Official("type-commode", "Commode", "Types", SearchConceptTarget.GenericText, "commode", "meuble à tiroirs", false),

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

    private static IReadOnlyList<SearchSynonymEntry> BuildOfficialSynonyms()
    {
        var result = new List<SearchSynonymEntry>();
        foreach (var concept in OfficialConcepts)
        foreach (var alias in SplitAliases(concept.OfficialAliasesCsv))
            result.Add(new SearchSynonymEntry
            {
                Id = $"syn-{concept.Id}-{StableId(alias)}", ConceptId = concept.Id, Term = alias,
                IsOfficial = true, IsActive = true
            });
        return result;
    }

    private static IReadOnlyList<SearchExpressionRecord> BuildOfficialExpressions()
    {
        static SearchExpressionRecord Rule(string id, string phrase, string concepts, string description) => new()
        {
            Id = id, Phrase = phrase, ConceptIdsCsv = concepts, Description = description,
            IsOfficial = true, IsActive = true
        };
        return
        [
            Rule("expr-armoire-rangement", "armoire de rangement", "type-armoire,element-rayon", "Armoire avec au moins un rayon"),
            Rule("expr-placard-rangement", "placard de rangement", "type-placard,element-rayon", "Placard avec au moins un rayon"),
            Rule("expr-rangement-etageres", "rangement avec étagères", "element-rayon", "Au moins un rayon"),
            Rule("expr-porte-relevable", "porte relevable", "element-relevant", "Relevant uniquement, sans compter une porte battante"),
            Rule("expr-ouvrant-haut", "ouvrant vers le haut", "element-relevant", "Relevant"),
            Rule("expr-porte-abattante", "porte abattante", "element-abattant", "Abattant uniquement"),
            Rule("expr-ouvrant-bas", "ouvrant vers le bas", "element-abattant", "Abattant"),
            Rule("expr-tiroir-anglais", "tiroir à l anglaise", "element-tiroir-anglais", "Tiroir intérieur"),
            Rule("expr-tiroir-interieur", "tiroir intérieur", "element-tiroir-anglais", "Tiroir à l’anglaise"),
            Rule("expr-sous-evier", "sous évier", "usage-sous-evier", "Usage sous-évier"),
            Rule("expr-meuble-evier", "meuble évier", "usage-sous-evier", "Usage sous-évier"),
            Rule("expr-sous-vasque", "sous vasque", "usage-vasque", "Usage vasque"),
            Rule("expr-meuble-vasque", "meuble vasque", "usage-vasque", "Usage vasque"),
            Rule("expr-meuble-bas", "meuble bas", "type-bas", "Préférence meuble bas"),
            Rule("expr-element-bas", "élément bas", "type-bas", "Préférence meuble bas"),
            Rule("expr-meuble-haut", "meuble haut", "type-haut", "Préférence meuble haut"),
            Rule("expr-element-haut", "élément haut", "type-haut", "Préférence meuble haut"),
            Rule("expr-demi-colonne", "demi colonne", "type-demi-colonne", "Préférence demi-colonne"),
            Rule("expr-armoire-four", "armoire four", "type-armoire,usage-four", "Armoire équipée pour un four"),
            Rule("expr-colonne-four", "colonne four", "type-colonne,usage-four", "Colonne équipée pour un four"),
            Rule("expr-colonne-four-mo", "colonne four micro onde", "type-colonne,usage-four,usage-micro-ondes", "Colonne four et micro-ondes"),
            Rule("expr-meuble-mo", "meuble micro onde", "usage-micro-ondes", "Usage micro-ondes"),
            Rule("expr-lave-vaisselle", "lave vaisselle", "usage-lave-vaisselle", "Usage lave-vaisselle"),
            Rule("expr-lave-linge", "lave linge", "usage-lave-linge", "Usage lave-linge"),
            Rule("expr-seche-linge", "sèche linge", "usage-seche-linge", "Usage sèche-linge"),
            Rule("expr-tri-selectif", "tri sélectif", "usage-poubelle", "Usage déchets et tri"),
            Rule("expr-meuble-tv", "meuble tv", "usage-tv", "Usage télévision et multimédia"),
            Rule("expr-tablette-reglable", "tablette réglable", "element-tablette-reglable", "Tablette réglable"),
            Rule("expr-tablette-mobile", "tablette mobile", "element-tablette-reglable", "Tablette réglable"),
            Rule("expr-tablette-fixe", "tablette fixe", "element-tablette-fixe", "Tablette fixe"),
            Rule("expr-porte-coulissante", "porte coulissante", "element-porte-coulissante", "Porte coulissante"),
            Rule("expr-porte-pliante", "porte pliante", "element-porte-pliante", "Porte pliante"),
            Rule("expr-porte-accordeon", "porte accordéon", "element-porte-pliante", "Porte pliante"),
            Rule("expr-porte-escamotable", "porte escamotable", "element-porte-escamotable", "Porte escamotable"),
            Rule("expr-porte-rentrante", "porte rentrante", "element-porte-escamotable", "Porte escamotable"),
            Rule("expr-panier-coulissant", "panier coulissant", "element-panier-coulissant", "Panier extractible"),
            Rule("expr-plateau-tournant", "plateau tournant", "element-plateau-tournant", "Plateau d’angle tournant"),
            Rule("expr-tringle-basculante", "tringle basculante", "element-tringle-basculante", "Penderie descendante"),
            Rule("expr-porte-pantalons", "porte pantalons", "element-porte-pantalons", "Porte-pantalons"),
            Rule("expr-au-dessus", "au dessus", "position-dessus", "Position au-dessus"),
            Rule("expr-en-dessous", "en dessous", "position-dessous", "Position en dessous"),
            Rule("expr-partie-haute", "en partie haute", "position-haut", "Position haute"),
            Rule("expr-partie-basse", "en partie basse", "position-bas", "Position basse"),
            Rule("expr-a-gauche", "à gauche", "position-gauche", "Position à gauche"),
            Rule("expr-a-droite", "à droite", "position-droite", "Position à droite"),
            Rule("expr-dossiers-suspendus", "dossiers suspendus", "usage-dossiers", "Rangement de dossiers suspendus"),
            Rule("expr-dos-applique", "dos en applique", "dos-applique", "Dos appliqué"),
            Rule("expr-fond-applique", "fond en applique", "dos-applique", "Dos appliqué"),
            Rule("expr-dos-rainure", "dos en rainure", "dos-rainure", "Dos rainuré"),
            Rule("expr-fond-rainure", "fond rainuré", "dos-rainure", "Dos rainuré"),
            Rule("expr-sans-dos", "sans dos", "dos-sans", "Meuble sans dos"),
            Rule("expr-sans-fond", "sans fond", "dos-sans", "Meuble sans dos"),
            Rule("expr-tourillons-vis", "tourillons vis", "assemblage-tourillons-vis", "Assemblage tourillons et vis"),
            Rule("expr-tourillon-vis", "tourillon vis", "assemblage-tourillons-vis", "Assemblage tourillons et vis"),
            Rule("expr-banque-accueil", "banque d accueil", "type-comptoir", "Comptoir ou banque d’accueil"),
            Rule("expr-meuble-caisse", "meuble caisse", "type-comptoir,usage-caisse", "Comptoir de caisse")
        ];
    }

    private static IReadOnlyList<SearchIgnoredTermRecord> BuildOfficialIgnoredTerms()
    {
        var terms = new[]
        {
            "je", "j", "nous", "on", "cherche", "recherche", "veux", "voudrais", "souhaite", "aimerais", "besoin",
            "faudrait", "faut", "falloir", "me", "moi", "trouve moi", "montre moi", "il me faut", "si possible",
            "s il te plaît", "s il vous plaît", "un", "une", "des", "du", "de la", "le", "la", "les", "d", "l",
            "pour", "dans", "sur", "au", "aux", "qui", "soit", "sont", "est", "et", "ayant", "contenant", "équipé",
            "équipée", "genre", "modèle", "quelque chose", "meuble", "mobilier"
        };
        return terms.Select((term, index) => new SearchIgnoredTermRecord
        {
            Id = $"ignore-{index:000}", Term = term, IsOfficial = true, IsActive = true
        }).ToList();
    }

    private static SearchConceptRecord Official(string id, string label, string category, SearchConceptTarget target, string value, string aliases, bool strict = true) => new()
    {
        Id = id, Label = label, Category = category, Target = target, TargetValue = value, OfficialAliasesCsv = aliases,
        IsOfficial = true, IsActive = true, IsStrict = strict
    };
}
