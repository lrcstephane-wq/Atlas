using System.Text.Json;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public static class CapabilityTagStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string GetPath(string sharedRoot) => Path.Combine(sharedRoot, "Configuration", "capability-tags.json");

    public static async Task<List<CapabilityTagRecord>> LoadAsync(string sharedRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedRoot)) return [];
        Directory.CreateDirectory(Path.Combine(sharedRoot, "Configuration"));
        var path = GetPath(sharedRoot);
        if (!File.Exists(path)) return [];
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<List<CapabilityTagRecord>>(stream, Options, cancellationToken) ?? [];
    }

    public static async Task SaveAsync(string sharedRoot, IEnumerable<CapabilityTagRecord> tags, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedRoot)) throw new InvalidOperationException("Le dossier partagé Atlas n’est pas renseigné.");
        Directory.CreateDirectory(Path.Combine(sharedRoot, "Configuration"));
        var path = GetPath(sharedRoot);
        var temporary = path + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, tags.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToList(), Options, cancellationToken);
        File.Move(temporary, path, true);
    }

    public static IReadOnlyList<CapabilityTagRecord> Resolve(ComponentRecord component, IEnumerable<CapabilityTagRecord> tags)
    {
        component.NormalizeCapabilities();
        var family = component.EffectiveFamilyName;
        var removed = component.RemovedInheritedCapabilityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = component.AddedCapabilityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tags.Where(tag => tag.IsActive &&
                    ((tag.DefaultFamilyNames.Any(f => f.Equals(family, StringComparison.OrdinalIgnoreCase)) && !removed.Contains(tag.Id)) || added.Contains(tag.Id)))
            .DistinctBy(tag => tag.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void RebuildLegacyCapabilities(ComponentRecord component, IEnumerable<CapabilityTagRecord> tags)
    {
        component.CapabilitiesCsv = string.Join(", ", Resolve(component, tags).Select(x => x.Label));
    }
}

