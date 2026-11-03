using System.Text.Json;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed class AtlasReferenceDataStore(string sharedRoot)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string FilePath => Path.Combine(sharedRoot, "Content", "Official", "reference-data.atlas.json");

    public async Task<AtlasReferenceData> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return new AtlasReferenceData();
        await using var stream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var data = await JsonSerializer.DeserializeAsync<AtlasReferenceData>(stream, Options, cancellationToken) ?? new();
        Normalize(data);
        return data;
    }

    private static void Normalize(AtlasReferenceData data)
    {
        data.FurnitureForms = Clean(data.FurnitureForms);
        data.ConstructionPrinciples = Clean(data.ConstructionPrinciples);
        data.BackPositions = Clean(data.BackPositions);
        data.AssemblyTypes = Clean(data.AssemblyTypes);
        data.TagCategories = Clean(data.TagCategories);
        data.DoorTypes = Clean(data.DoorTypes);
        data.DrawerTypes = Clean(data.DrawerTypes);
        data.SearchConceptCategories = Clean(data.SearchConceptCategories);
    }

    private static List<string> Clean(IEnumerable<string>? values) => (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
