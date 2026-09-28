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
        catalog.Furniture ??= [];
        foreach (var item in catalog.Furniture) item.PersonalTagIds ??= [];
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
        if (string.IsNullOrWhiteSpace(catalog.FurnitureRoot) || !Directory.Exists(catalog.FurnitureRoot)) return 0;

        var files = Directory.EnumerateFiles(catalog.FurnitureRoot, "*.top", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existing = catalog.Furniture
            .Where(item => !string.IsNullOrWhiteSpace(item.RelativeTopPath))
            .ToDictionary(item => NormalizeRelative(item.RelativeTopPath), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var file in files)
        {
            var relative = NormalizeRelative(Path.GetRelativePath(catalog.FurnitureRoot, file));
            seen.Add(relative);
            if (!existing.TryGetValue(relative, out var item))
            {
                item = new CustomerFurnitureItem
                {
                    DisplayName = HumanizeFileName(Path.GetFileNameWithoutExtension(file)),
                    RelativeTopPath = relative
                };
                catalog.Furniture.Add(item);
                added++;
            }

            item.LastSeenUtc = DateTimeOffset.UtcNow;
            var image = FindPreview(file);
            if (image is not null) item.ImageRelativePath = NormalizeRelative(Path.GetRelativePath(catalog.FurnitureRoot, image));
        }

        catalog.Furniture.RemoveAll(item => !seen.Contains(NormalizeRelative(item.RelativeTopPath)));
        return added;
    }

    public static string ResolvePath(CustomerFurnitureCatalog catalog, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
        return Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(catalog.FurnitureRoot, relativePath);
    }

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
