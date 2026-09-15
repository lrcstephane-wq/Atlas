using System.Text.Json;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public static class ComponentTaxonomyStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string GetPath(string sharedRoot) => Path.Combine(sharedRoot, "Configuration", "component-taxonomy.json");

    public static async Task<ComponentTaxonomy> LoadAsync(string sharedRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedRoot)) return new();
        Directory.CreateDirectory(Path.Combine(sharedRoot, "Configuration"));
        var path = GetPath(sharedRoot);
        if (!File.Exists(path)) return await MigrateLegacyAsync(sharedRoot, cancellationToken);
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var taxonomy = await JsonSerializer.DeserializeAsync<ComponentTaxonomy>(stream, Options, cancellationToken) ?? new();
        Normalize(taxonomy);
        return taxonomy;
    }

    public static async Task SaveAsync(string sharedRoot, ComponentTaxonomy taxonomy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sharedRoot)) throw new InvalidOperationException("Le dossier partagé Atlas n’est pas renseigné.");
        Normalize(taxonomy);
        Directory.CreateDirectory(Path.Combine(sharedRoot, "Configuration"));
        var path = GetPath(sharedRoot);
        var temporary = path + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        taxonomy.Families = taxonomy.Families.OrderBy(x => x.LibraryName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        taxonomy.Tags = taxonomy.Tags.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, taxonomy, Options, cancellationToken);
        File.Move(temporary, path, true);
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

    public static string FamilyKey(string libraryName, string familyName) => $"{libraryName.Trim()}|{familyName.Trim()}";

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

    private static async Task<ComponentTaxonomy> MigrateLegacyAsync(string sharedRoot, CancellationToken cancellationToken)
    {
        var taxonomy = new ComponentTaxonomy();
        foreach (var legacy in await CapabilityTagStore.LoadAsync(sharedRoot, cancellationToken))
            taxonomy.Tags.Add(new ComponentTagRecord { Id = legacy.Id, Label = legacy.Label, Description = legacy.Description, IsActive = legacy.IsActive });
        return taxonomy;
    }
}
