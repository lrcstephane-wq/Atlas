using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed record FurnitureSearchCandidate(
    FurnitureRecord Furniture,
    IReadOnlyCollection<string> Usages,
    IReadOnlyCollection<string> Tags,
    CatalogSearchDocument Document);

public sealed record FurnitureSearchEvaluation(bool IsMatch, double Score, string Explanation, string RejectionReason)
{
    public static FurnitureSearchEvaluation NoMatch(string reason) => new(false, 0d, string.Empty, reason);
}

public static class FurnitureSearchMatcher
{
    public static FurnitureSearchEvaluation Evaluate(
        FurnitureSearchCandidate candidate,
        SearchIntent intent,
        SearchDictionaryDocument dictionary,
        IEnumerable<string> protectedTerms)
    {
        var reasons = new List<string>();
        foreach (var constraint in intent.Constraints)
        {
            if (!MatchesConstraint(candidate, constraint, dictionary))
                return FurnitureSearchEvaluation.NoMatch($"Critère absent : {constraint.ConceptLabel}");
            reasons.Add(constraint.ConceptLabel);
        }

        var textScore = 1d;
        if (!string.IsNullOrWhiteSpace(intent.RemainingQuery))
        {
            textScore = CatalogSearchEngine.Score(intent.RemainingQuery, candidate.Document, protectedTerms: protectedTerms);
            if (textScore <= 0d) return FurnitureSearchEvaluation.NoMatch($"Texte absent : {intent.RemainingQuery}");
            reasons.Add($"texte « {intent.RemainingQuery} »");
        }

        var score = string.IsNullOrWhiteSpace(intent.RemainingQuery) ? 1d : Math.Min(1.22d, 0.72d + textScore * 0.28d);
        return new FurnitureSearchEvaluation(true, score,
            reasons.Count == 0 ? "Correspondance textuelle" : "Trouvé grâce à : " + string.Join(" · ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)),
            string.Empty);
    }

    public static bool MatchesConstraint(FurnitureSearchCandidate candidate, SearchConstraint constraint,
        SearchDictionaryDocument dictionary)
    {
        var furniture = candidate.Furniture;
        var matches = constraint.Target switch
        {
            SearchConceptTarget.DoorCount => QuantityMatches(candidate, furniture.Porte, furniture.NombrePortes, constraint, dictionary),
            SearchConceptTarget.DrawerCount => QuantityMatches(candidate, furniture.Tiroir, furniture.NombreTiroirs, constraint, dictionary),
            SearchConceptTarget.InnerDrawerCount => QuantityMatches(candidate, furniture.TiroirAnglaise, furniture.NombreTiroirsAnglaise, constraint, dictionary),
            SearchConceptTarget.FlapCount => QuantityMatches(candidate, furniture.Abattant, furniture.NombreAbattants, constraint, dictionary),
            SearchConceptTarget.LiftUpCount => QuantityMatches(candidate, furniture.Relevant, furniture.NombreRelevants, constraint, dictionary),
            SearchConceptTarget.ShelfCount => QuantityMatches(candidate, furniture.Rayon, furniture.NombreRayons, constraint, dictionary),
            SearchConceptTarget.OpenNicheCount => QuantityMatches(candidate, furniture.NicheOuverte, furniture.NombreNichesOuvertes, constraint, dictionary),
            SearchConceptTarget.Hanging => furniture.Penderie || StrictSearchContains(candidate.Document, constraint, dictionary),
            SearchConceptTarget.HorizontalSeparation => furniture.SeparationHorizontale || StrictSearchContains(candidate.Document, constraint, dictionary),
            SearchConceptTarget.VerticalSeparation => furniture.SeparationVerticale || StrictSearchContains(candidate.Document, constraint, dictionary),
            SearchConceptTarget.FurnitureType => FurnitureTypeMatches(furniture.TypeMeuble, constraint.Value),
            SearchConceptTarget.FurnitureUsage => candidate.Usages.Any(value => ValueMatches(value, constraint.Value))
                                                   || StrictSearchContains(candidate.Document, constraint, dictionary),
            SearchConceptTarget.FurnitureTag => candidate.Tags.Any(value => ValueMatches(value, constraint.Value)),
            SearchConceptTarget.FurnitureForm => ValueMatches(furniture.Forme, constraint.Value),
            SearchConceptTarget.Construction => ValueMatches(furniture.PrincipleConstruction, constraint.Value),
            SearchConceptTarget.Assembly => ValueMatches(furniture.TypeAssemblage, constraint.Value),
            SearchConceptTarget.BackPosition => ValueMatches(furniture.PositionDos, constraint.Value),
            SearchConceptTarget.SpatialRelation => StrictSearchContains(candidate.Document, constraint, dictionary),
            SearchConceptTarget.GenericText => StrictSearchContains(candidate.Document, constraint, dictionary),
            _ => false
        };

        return constraint.Mode == SearchConstraintMode.Exclude && !SearchDictionaryService.IsQuantityTarget(constraint.Target)
            ? !matches
            : matches;
    }

    private static bool QuantityMatches(FurnitureSearchCandidate candidate, bool isPresent, int quantity,
        SearchConstraint constraint, SearchDictionaryDocument dictionary)
    {
        var actual = SearchDictionaryService.EffectiveQuantity(isPresent, quantity);
        if (actual == 0) actual = InferQuantity(candidate.Document, TermsFor(constraint, dictionary));
        return SearchDictionaryService.QuantityMatches(actual, constraint);
    }

    private static int InferQuantity(CatalogSearchDocument document, IEnumerable<string> terms)
    {
        var best = 0;
        foreach (var term in terms)
        {
            var termTokens = CatalogSearchEngine.Normalize(term).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (termTokens.Length == 0) continue;
            foreach (var field in document.Fields)
            for (var start = 0; start <= field.Tokens.Length - termTokens.Length; start++)
            {
                var matches = true;
                for (var offset = 0; offset < termTokens.Length; offset++)
                    if (!field.Tokens[start + offset].Equals(termTokens[offset], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                if (!matches) continue;
                var inferred = start > 0 && ParseNumber(field.Tokens[start - 1]) is { } number ? number : 1;
                best = Math.Max(best, inferred);
            }
        }
        return best;
    }

    private static int? ParseNumber(string token)
    {
        if (int.TryParse(token, out var number)) return number;
        return token switch
        {
            "zero" => 0, "un" or "une" => 1, "deux" => 2, "trois" => 3, "quatre" => 4,
            "cinq" => 5, "six" => 6, "sept" => 7, "huit" => 8, "neuf" => 9, "dix" => 10,
            "onze" => 11, "douze" => 12, _ => null
        };
    }

    private static bool StrictSearchContains(CatalogSearchDocument document, SearchConstraint constraint,
        SearchDictionaryDocument dictionary)
    {
        return TermsFor(constraint, dictionary).Any(term => CatalogSearchEngine.ContainsExactPhrase(term, document));
    }

    private static IEnumerable<string> TermsFor(SearchConstraint constraint, SearchDictionaryDocument dictionary)
    {
        var concept = dictionary.Concepts.FirstOrDefault(item => item.IsActive &&
            item.Label.Equals(constraint.ConceptLabel, StringComparison.OrdinalIgnoreCase));
        return new[] { constraint.Value }
            .Concat(concept?.Expressions() ?? Enumerable.Empty<string>())
            .Concat(concept is null ? Enumerable.Empty<string>() : dictionary.Synonyms
                .Where(item => item.IsActive && item.ConceptId.Equals(concept.Id, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Term))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ValueMatches(string? actual, string expected)
    {
        var actualValue = CatalogSearchEngine.Normalize(actual);
        var expectedValue = CatalogSearchEngine.Normalize(expected);
        return actualValue.Equals(expectedValue, StringComparison.Ordinal)
               || $" {actualValue} ".Contains($" {expectedValue} ", StringComparison.Ordinal);
    }

    private static bool FurnitureTypeMatches(string? actual, string expected)
    {
        if (ValueMatches(actual, expected)) return true;
        var actualValue = CatalogSearchEngine.Normalize(actual);
        var expectedValue = CatalogSearchEngine.Normalize(expected);
        return (expectedValue is "colonne" or "armoire") && (actualValue is "colonne" or "demi colonne" or "armoire");
    }
}
