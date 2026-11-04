using System.Text.Json;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed class CustomerFurnitureStore(string sharedRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string CatalogPath => Path.Combine(sharedRoot, "Data", "customer-furniture.atlas.json");

    public async Task<CustomerFurnitureCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CatalogPath)) return new CustomerFurnitureCatalog();
        await using var stream = File.Open(CatalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var catalog = await JsonSerializer.DeserializeAsync<CustomerFurnitureCatalog>(stream, JsonOptions, cancellationToken)
            ?? new CustomerFurnitureCatalog();
        catalog.PersonalTags ??= [];
        catalog.Locations ??= [];
        catalog.ClientUniverses ??= [];
        catalog.ClientTags ??= [];
        catalog.Exclusions ??= [];
        catalog.Furniture ??= [];
        if (catalog.Locations.Count == 0 && !string.IsNullOrWhiteSpace(catalog.FurnitureRoot))
        {
            catalog.Locations.Add(new CustomerFurnitureLocation { Name = "Meubles Client", RootPath = catalog.FurnitureRoot });
            catalog.FurnitureRoot = string.Empty;
        }
        if (catalog.ClientTags.Count == 0 && catalog.PersonalTags.Count > 0)
            catalog.ClientTags = catalog.PersonalTags;
        var legacyLocationId = catalog.Locations.FirstOrDefault()?.Id ?? string.Empty;
        foreach (var item in catalog.Furniture)
        {
            item.PersonalTagIds ??= [];
            item.AtlasUniverseIds ??= [];
            item.ClientUniverseIds ??= [];
            item.AtlasTagIds ??= [];
            item.ClientTagIds ??= [];
            if (item.ClientTagIds.Count == 0 && item.PersonalTagIds.Count > 0) item.ClientTagIds = item.PersonalTagIds.ToList();
            if (string.IsNullOrWhiteSpace(item.LocationId)) item.LocationId = legacyLocationId;
        }
        catalog.SchemaVersion = 2;
        return catalog;
    }

    public async Task SaveAsync(CustomerFurnitureCatalog catalog, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
        var temporary = CatalogPath + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
        File.Move(temporary, CatalogPath, true);
    }

    public int Synchronize(CustomerFurnitureCatalog catalog)
    {
        if (catalog.Locations.Count == 0 && !string.IsNullOrWhiteSpace(catalog.FurnitureRoot))
        {
            catalog.Locations.Add(new CustomerFurnitureLocation { Name = "Meubles Client", RootPath = catalog.FurnitureRoot });
            catalog.FurnitureRoot = string.Empty;
        }
        var existing = catalog.Furniture
            .Where(item => !string.IsNullOrWhiteSpace(item.RelativeTopPath))
            .GroupBy(ItemKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var exclusions = catalog.Exclusions.Select(item => ItemKey(item.LocationId, item.RelativeTopPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var location in catalog.Locations.Where(item => item.IsActive && Directory.Exists(item.RootPath)))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(location.RootPath, "*.top", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var relative = NormalizeRelative(Path.GetRelativePath(location.RootPath, file));
                var key = ItemKey(location.Id, relative);
                seen.Add(key);
                if (exclusions.Contains(key)) continue;
                if (!existing.TryGetValue(key, out var item))
                {
                    item = new CustomerFurnitureItem
                    {
                        LocationId = location.Id,
                        DisplayName = HumanizeFileName(Path.GetFileNameWithoutExtension(file)),
                        RelativeTopPath = relative
                    };
                    catalog.Furniture.Add(item);
                    existing[key] = item;
                    added++;
                }

                item.LastSeenUtc = DateTimeOffset.UtcNow;
                var image = FindPreview(file);
                if (image is not null) item.ImageRelativePath = NormalizeRelative(Path.GetRelativePath(location.RootPath, image));
            }
            catalog.Furniture.RemoveAll(item => item.LocationId.Equals(location.Id, StringComparison.OrdinalIgnoreCase) && !seen.Contains(ItemKey(item)));
        }
        return added;
    }

    public static string ResolvePath(CustomerFurnitureCatalog catalog, CustomerFurnitureItem item, string? relativePath = null)
    {
        relativePath ??= item.RelativeTopPath;
        if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
        if (Path.IsPathRooted(relativePath)) return relativePath;
        var root = catalog.Locations.FirstOrDefault(location => location.Id.Equals(item.LocationId, StringComparison.OrdinalIgnoreCase))?.RootPath
            ?? catalog.FurnitureRoot;
        return string.IsNullOrWhiteSpace(root) ? relativePath : Path.Combine(root, relativePath);
    }

    public static string ItemKey(CustomerFurnitureItem item) => ItemKey(item.LocationId, item.RelativeTopPath);
    public static string ItemKey(string locationId, string relativePath) => $"{locationId}|{NormalizeRelative(relativePath)}";

    private static string? FindPreview(string topPath)
    {
        var candidates = new[]
        {
            topPath + ".png", topPath + ".jpg", topPath + ".jpeg",
            Path.ChangeExtension(topPath, ".png"), Path.ChangeExtension(topPath, ".jpg"), Path.ChangeExtension(topPath, ".jpeg")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NormalizeRelative(string value) => value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim();

    private static string HumanizeFileName(string value) => string.Join(' ', value.Replace('_', ' ').Replace('-', ' ')
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
