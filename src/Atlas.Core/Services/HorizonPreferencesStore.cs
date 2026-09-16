using System.Text.Json;

namespace Atlas.Core.Services;

public sealed class HorizonPreferences
{
    public HashSet<string> FavoriteFacetKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class HorizonPreferencesStore(string sharedRoot, string userId)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private string FilePath => Path.Combine(sharedRoot, "Configuration", "Horizon", $"{SafeFileName(userId)}.json");

    public async Task<HorizonPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return new HorizonPreferences();
        await using var stream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var preferences = await JsonSerializer.DeserializeAsync<HorizonPreferences>(stream, JsonOptions, cancellationToken) ?? new HorizonPreferences();
        preferences.FavoriteFacetKeys = new HashSet<string>(preferences.FavoriteFacetKeys ?? [], StringComparer.OrdinalIgnoreCase);
        return preferences;
    }

    public async Task SaveAsync(HorizonPreferences preferences, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken);
        File.Move(temporary, FilePath, true);
    }

    private static string SafeFileName(string value) => string.Concat((string.IsNullOrWhiteSpace(value) ? "default" : value).Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
