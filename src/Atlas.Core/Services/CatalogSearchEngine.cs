using System.Globalization;
using System.Text;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed record CatalogSearchField(string Text, double Weight = 1d);

public static class CatalogSearchEngine
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    public static double Score(string query, IEnumerable<CatalogSearchField> fields, IEnumerable<SearchSynonymRecord>? synonyms = null)
    {
        var normalizedQuery = Normalize(query);
        var queryTokens = Tokens(normalizedQuery);
        if (queryTokens.Length == 0) return 1d;

        var searchableFields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Text) && field.Weight > 0)
            .Select(field => new PreparedField(Normalize(field.Text), Math.Clamp(field.Weight, 0.1d, 2d)))
            .Where(field => field.Tokens.Length > 0)
            .ToArray();
        if (searchableFields.Length == 0) return 0d;

        var synonymGroups = PrepareSynonyms(synonyms);
        double total = 0;
        foreach (var queryToken in queryTokens)
        {
            var alternatives = ExpandToken(queryToken, synonymGroups);
            var best = searchableFields.Max(field => alternatives.Max(alternative =>
                field.Tokens.Max(candidate => Similarity(alternative, candidate)) * field.Weight));
            if (best < MinimumSimilarity(queryToken.Length)) return 0d;
            total += Math.Min(1d, best);
        }

        var score = total / queryTokens.Length;
        if (searchableFields.Any(field => field.Normalized.Contains(normalizedQuery, StringComparison.Ordinal))) score += 0.22d;
        return Math.Min(1.22d, score);
    }

    public static string CorrectQuery(string query, IEnumerable<string> vocabulary, IEnumerable<SearchSynonymRecord>? synonyms = null)
    {
        var words = vocabulary.SelectMany(value => Tokens(Normalize(value)))
            .Concat(PrepareSynonyms(synonyms).SelectMany(group => group))
            .Where(word => word.Length > 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (words.Length == 0) return Normalize(query);

        return string.Join(' ', Tokens(Normalize(query)).Select(token =>
        {
            if (words.Contains(token, StringComparer.Ordinal)) return token;
            var match = words.Select(word => (Word: word, Similarity: Similarity(token, word)))
                .OrderByDescending(item => item.Similarity)
                .ThenBy(item => item.Word.Length)
                .FirstOrDefault();
            return match.Similarity >= MinimumSimilarity(token.Length) ? match.Word : token;
        }));
    }

    private static string[][] PrepareSynonyms(IEnumerable<SearchSynonymRecord>? synonyms) =>
        (synonyms ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Canonical))
            .Select(item => Tokens(Normalize($"{item.Canonical} {item.AliasesCsv}")))
            .Where(group => group.Length > 1)
            .ToArray();

    private static string[] ExpandToken(string token, IEnumerable<string[]> groups)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { token };
        foreach (var group in groups)
        {
            if (group.Any(candidate => Similarity(token, candidate) >= MinimumSimilarity(token.Length)))
                result.UnionWith(group);
        }
        return result.ToArray();
    }

    private static string[] Tokens(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static double MinimumSimilarity(int length) => length switch
    {
        <= 3 => 1d,
        <= 5 => 0.74d,
        <= 8 => 0.66d,
        _ => 0.62d
    };

    private static double Similarity(string left, string right)
    {
        if (left == right) return 1d;
        if (left.Length == 0 || right.Length == 0) return 0d;
        if ((left.Length >= 4 && right.StartsWith(left, StringComparison.Ordinal)) ||
            (right.Length >= 4 && left.StartsWith(right, StringComparison.Ordinal))) return 0.91d;
        var distance = DamerauLevenshtein(left, right);
        return Math.Max(0d, 1d - (double)distance / Math.Max(left.Length, right.Length));
    }

    private static int DamerauLevenshtein(string source, string target)
    {
        var matrix = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++) matrix[i, 0] = i;
        for (var j = 0; j <= target.Length; j++) matrix[0, j] = j;
        for (var i = 1; i <= source.Length; i++)
        for (var j = 1; j <= target.Length; j++)
        {
            var cost = source[i - 1] == target[j - 1] ? 0 : 1;
            matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1), matrix[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                matrix[i, j] = Math.Min(matrix[i, j], matrix[i - 2, j - 2] + cost);
        }
        return matrix[source.Length, target.Length];
    }

    private sealed record PreparedField(string Normalized, double Weight)
    {
        public string[] Tokens { get; } = CatalogSearchEngine.Tokens(Normalized);
    }
}
