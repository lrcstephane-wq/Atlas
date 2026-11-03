using System.Text.Json;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public static class ComponentTaxonomyStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string GetLegacyPath(string sharedRoot) => Path.Combine(sharedRoot, "Configuration", "component-taxonomy.json");
    public static string GetPath(string sharedRoot) => Path.Combine(sharedRoot, "Configuration", "component-taxonomy.site.json");
    public static string GetOfficialPath(string sharedRoot) => Path.Combine(sharedRoot, "Content", "Official", "component-taxonomy.json");

    public static async Task<ComponentTaxonomy> LoadAsync(string sharedRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedRoot)) return new();
        Directory.CreateDirectory(Path.Combine(sharedRoot, "Configuration"));
        await EnsureLayeredAsync(sharedRoot, cancellationToken);
        var official = await ReadAsync<ComponentTaxonomy>(GetOfficialPath(sharedRoot), cancellationToken) ?? new();
        var site = await ReadAsync<ComponentTaxonomySiteOverlay>(GetPath(sharedRoot), cancellationToken) ?? new();
        Normalize(official);
        var taxonomy = new ComponentTaxonomy
        {
            SchemaVersion = Math.Max(official.SchemaVersion, 2),
            Families = Merge(official.Families, site.Families, site.RemovedFamilyIds, item => item.Id),
            Tags = Merge(official.Tags, site.Tags, site.RemovedTagIds, item => item.Id)
        };
        Normalize(taxonomy);
        return taxonomy;
    }

    public static async Task SaveAsync(string sharedRoot, ComponentTaxonomy taxonomy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedRoot)) throw new InvalidOperationException("Le dossier partagé Atlas n’est pas renseigné.");
        Normalize(taxonomy);
        await EnsureLayeredAsync(sharedRoot, cancellationToken);
        var official = await ReadAsync<ComponentTaxonomy>(GetOfficialPath(sharedRoot), cancellationToken) ?? new();
        Normalize(official);
        var overlay = new ComponentTaxonomySiteOverlay
        {
            Families = Differences(official.Families, taxonomy.Families, item => item.Id),
            RemovedFamilyIds = Removed(official.Families, taxonomy.Families, item => item.Id),
            Tags = Differences(official.Tags, taxonomy.Tags, item => item.Id),
            RemovedTagIds = Removed(official.Tags, taxonomy.Tags, item => item.Id)
        };
        overlay.Families = overlay.Families.OrderBy(x => x.LibraryName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        overlay.Tags = overlay.Tags.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        await WriteAtomicAsync(GetPath(sharedRoot), overlay, cancellationToken);
    }

    public static int SyncDetectedFamilies(ComponentTaxonomy taxonomy, IEnumerable<ComponentRecord> components)
    {
        Normalize(taxonomy);
        foreach (var family in taxonomy.Families.Where(x => x.IsDetected)) family.IsMissing = true;
        foreach (var type in taxonomy.Families.SelectMany(x => x.Types).Where(x => x.IsDetected)) type.IsMissing = true;
        var added = 0;
        foreach (var group in components.Where(x => !x.IsDemo && !string.IsNullOrWhiteSpace(x.LibraryName) && !string.IsNullOrWhiteSpace(x.FamilyName))
                     .GroupBy(x => FamilyKey(x.LibraryName, x.FamilyName), StringComparer.OrdinalIgnoreCase))
        {
            var sample = group.First();
            var family = taxonomy.Families.FirstOrDefault(x => FamilyKey(x.LibraryName, x.Name).Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (family is null)
            {
                family = new ComponentFamilyRecord { LibraryName = sample.LibraryName, Name = sample.FamilyName, IsDetected = true };
                taxonomy.Families.Add(family);
                added++;
            }
            family.IsDetected = true;
            family.IsMissing = false;
            foreach (var typeName in group.Select(x => x.TypeCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var type = family.Types.FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (type is null)
                {
                    type = new ComponentTypeRecord { Name = typeName, IsDetected = true };
                    family.Types.Add(type);
                }
                type.IsDetected = true;
                type.IsMissing = false;
            }
        }
        return added;
    }

    public static IReadOnlyList<ComponentTagRecord> Resolve(ComponentRecord component, ComponentTaxonomy taxonomy)
    {
        Normalize(taxonomy);
        component.NormalizeTags();
        var added = component.AddedTagIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return taxonomy.Tags.Where(tag => tag.IsActive && added.Contains(tag.Id))
            .DistinctBy(tag => tag.Id, StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static IReadOnlyList<ComponentTagRecord> Resolve(FurnitureRecord furniture, IEnumerable<ComponentRecord> components, ComponentTaxonomy taxonomy)
    {
        furniture.AddedTagIds ??= [];
        furniture.RemovedInheritedTagIds ??= [];
        var inherited = components.Where(x => furniture.ComponentIds.Contains(x.Id)).SelectMany(x => Resolve(x, taxonomy)).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = furniture.RemovedInheritedTagIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = furniture.AddedTagIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return taxonomy.Tags.Where(tag => tag.IsActive && ((inherited.Contains(tag.Id) && !removed.Contains(tag.Id)) || added.Contains(tag.Id)))
            .DistinctBy(tag => tag.Id, StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static int ApplyDirectTags(IEnumerable<ComponentRecord> components, IEnumerable<string> tagIds, bool add)
    {
        var ids = tagIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var component in components)
        {
            component.NormalizeTags();
            var before = component.AddedTagIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                RemoveIgnoreCase(component.AddedTagIds, id);
                RemoveIgnoreCase(component.RemovedInheritedTagIds, id);
                component.RemovedInheritedTagReasons.Remove(id);
                if (add) component.AddedTagIds.Add(id);
            }
            if (!before.SetEquals(component.AddedTagIds)) changed++;
        }
        return changed;
    }

    public static string FamilyKey(string libraryName, string familyName) => $"{libraryName.Trim()}|{familyName.Trim()}";

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }

    private static void Normalize(ComponentTaxonomy taxonomy)
    {
        taxonomy.Families ??= [];
        taxonomy.Tags ??= [];
        foreach (var family in taxonomy.Families)
        {
            family.TagIds ??= [];
            family.Types ??= [];
            foreach (var type in family.Types) type.TagIds ??= [];
        }
        taxonomy.SchemaVersion = Math.Max(taxonomy.SchemaVersion, 2);
    }

    private static async Task EnsureLayeredAsync(string sharedRoot, CancellationToken cancellationToken)
    {
        var officialPath = GetOfficialPath(sharedRoot);
        if (File.Exists(officialPath)) return;
        ComponentTaxonomy taxonomy;
        var legacyPath = GetLegacyPath(sharedRoot);
        if (File.Exists(legacyPath))
        {
            taxonomy = await ReadAsync<ComponentTaxonomy>(legacyPath, cancellationToken) ?? new();
            Directory.CreateDirectory(Path.Combine(sharedRoot, "Backups"));
            File.Copy(legacyPath, Path.Combine(sharedRoot, "Backups", $"component-taxonomy-before-content-layers-{DateTime.Now:yyyyMMdd-HHmmss}.json"), false);
        }
        else
        {
            taxonomy = new ComponentTaxonomy();
            foreach (var legacy in await CapabilityTagStore.LoadAsync(sharedRoot, cancellationToken))
                taxonomy.Tags.Add(new ComponentTagRecord { Id = legacy.Id, Label = legacy.Label, Description = legacy.Description, IsActive = legacy.IsActive });
        }
        Normalize(taxonomy);
        await WriteAtomicAsync(officialPath, taxonomy, cancellationToken);
        if (!File.Exists(GetPath(sharedRoot))) await WriteAtomicAsync(GetPath(sharedRoot), new ComponentTaxonomySiteOverlay(), cancellationToken);
    }

    private static List<T> Merge<T>(IEnumerable<T> official, IEnumerable<T> changed, IEnumerable<string> removed, Func<T, string> key)
    {
        var blocked = removed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = changed.GroupBy(key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var result = new List<T>();
        foreach (var item in official)
        {
            var id = key(item);
            if (blocked.Contains(id)) continue;
            result.Add(Clone(overrides.Remove(id, out var replacement) ? replacement : item));
        }
        result.AddRange(overrides.Values.Select(item => Clone(item)));
        return result;
    }

    private static List<T> Differences<T>(IEnumerable<T> official, IEnumerable<T> current, Func<T, string> key)
    {
        var baseline = official.ToDictionary(key, item => JsonValue(item), StringComparer.OrdinalIgnoreCase);
        return current.Where(item => !baseline.TryGetValue(key(item), out var json) || json != JsonValue(item)).Select(item => Clone(item)).ToList();
    }

    private static List<string> Removed<T>(IEnumerable<T> official, IEnumerable<T> current, Func<T, string> key)
    {
        var present = current.Select(key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return official.Select(key).Where(id => !present.Contains(id)).ToList();
    }

    private static string JsonValue<T>(T value) => JsonSerializer.Serialize(value, Options);
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
        File.Move(temporary, path, true);
    }
}
