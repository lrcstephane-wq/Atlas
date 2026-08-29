namespace Atlas.Core.Services;

using Atlas.Core.Models;

public static class ComponentNameParser
{
    public static bool TryParse(string technicalName, out ParsedComponentName parsed)
    {
        var segments = technicalName.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var type = segments.FirstOrDefault() ?? string.Empty;
        var markers = segments.Skip(1)
            .Select(segment => segment.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.OrdinalIgnoreCase);

        markers.TryGetValue("V", out var variant);
        markers.TryGetValue("I", out var index);
        markers.TryGetValue("R", out var range);
        markers.TryGetValue("C", out var construction);
        parsed = new ParsedComponentName(type, variant ?? string.Empty, index ?? string.Empty, range ?? string.Empty, construction ?? string.Empty);
        return type.Length > 0 && parsed.Variant.Length > 0 && parsed.Index.Length > 0;
    }
}
