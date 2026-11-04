using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed record FurnitureSearchCandidate(
    FurnitureRecord Furniture,
    IReadOnlyCollection<string> Usages,
    IReadOnlyCollection<string> Tags,
    CatalogSearchDocument Document);

public sealed record FurnitureSearchEvaluation(bool IsMatch, double Score, string Explanation)
{
    public static FurnitureSearchEvaluation NoMatch { get; } = new(false, 0d, string.Empty);
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
            if (!MatchesConstraint(candidate, constraint, dictionary)) return FurnitureSearchEvaluation.NoMatch;
            reasons.Add(constraint.ConceptLabel);
        }

        var textScore = 1d;
        if (!string.IsNullOrWhiteSpace(intent.RemainingQuery))
        {
            textScore = CatalogSearchEngine.Score(intent.RemainingQuery, candidate.Document, protectedTerms: protectedTerms);
            if (textScore <= 0d) return FurnitureSearchEvaluation.NoMatch;
            reasons.Add($"texte « {intent.RemainingQuery} »");
        }

        var score = string.IsNullOrWhiteSpace(intent.RemainingQuery) ? 1d : Math.Min(1.22d, 0.72d + textScore * 0.28d);
        return new FurnitureSearchEvaluation(true, score,
            reasons.Count == 0 ? "Correspondance textuelle" : "Trouvé grâce à : " + string.Join(" · ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    public static bool MatchesConstraint(FurnitureSearchCandidate candidate, SearchConstraint constraint,
        SearchDictionaryDocument dictionary)
    {
        var furniture = candidate.Furniture;
        var matches = constraint.Target switch
        {
            SearchConceptTarget.DoorCount => QuantityMatches(furniture.Porte, furniture.NombrePortes, constraint),
            SearchConceptTarget.DrawerCount => QuantityMatches(furniture.Tiroir, furniture.NombreTiroirs, constraint),
            SearchConceptTarget.InnerDrawerCount => QuantityMatches(furniture.TiroirAnglaise, furniture.NombreTiroirsAnglaise, constraint),
            SearchConceptTarget.FlapCount => QuantityMatches(furniture.Abattant, furniture.NombreAbattants, constraint),
            SearchConceptTarget.LiftUpCount => QuantityMatches(furniture.Relevant, furniture.NombreRelevants, constraint),
            SearchConceptTarget.ShelfCount => QuantityMatches(furniture.Rayon, furniture.NombreRayons, constraint),
            SearchConceptTarget.OpenNicheCount => QuantityMatches(furniture.NicheOuverte, furniture.NombreNichesOuvertes, constraint),
            SearchConceptTarget.Hanging => furniture.Penderie,
            SearchConceptTarget.HorizontalSeparation => furniture.SeparationHorizontale,
            SearchConceptTarget.VerticalSeparation => furniture.SeparationVerticale,
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

    private static bool QuantityMatches(bool isPresent, int quantity, SearchConstraint constraint) =>
        SearchDictionaryService.QuantityMatches(SearchDictionaryService.EffectiveQuantity(isPresent, quantity), constraint);

    private static bool StrictSearchContains(CatalogSearchDocument document, SearchConstraint constraint,
        SearchDictionaryDocument dictionary)
    {
        var concept = dictionary.Concepts.FirstOrDefault(item =>
            item.Label.Equals(constraint.ConceptLabel, StringComparison.OrdinalIgnoreCase));
        var terms = new[] { constraint.Value }
            .Concat(concept?.Expressions() ?? Enumerable.Empty<string>())
            .Concat(concept is null ? Enumerable.Empty<string>() : dictionary.Synonyms
                .Where(item => item.IsActive && item.ConceptId.Equals(concept.Id, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Term))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return terms.Any(term => CatalogSearchEngine.ContainsExactPhrase(term, document));
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
