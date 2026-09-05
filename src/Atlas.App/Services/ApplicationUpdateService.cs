using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.App.Services;

public sealed class ApplicationUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/lrcstephane-wq/Atlas/releases/latest";
    private const string AssetName = "Atlas-win-x64.zip";
    private readonly HttpClient _httpClient = new();
    private ReleaseAsset? _availableAsset;

    public ApplicationUpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Biblideo-Atlas-Updater/0.2.1");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string CurrentVersion => GetCurrentVersion().ToString(3);
    public string? AvailableVersion { get; private set; }

    public async Task<string?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("La réponse GitHub est vide.");
        if (!Version.TryParse(release.TagName.Trim().TrimStart('v', 'V'), out var latest))
            throw new InvalidOperationException($"Version GitHub invalide : {release.TagName}");
        if (latest <= GetCurrentVersion()) return null;
        _availableAsset = release.Assets.FirstOrDefault(asset => asset.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"La release {release.TagName} ne contient pas {AssetName}.");
        AvailableVersion = latest.ToString(3);
        return AvailableVersion;
    }

    public void OpenDownloadPage()
    {
        if (_availableAsset is null) throw new InvalidOperationException("Aucune mise à jour disponible.");
        Process.Start(new ProcessStartInfo(_availableAsset.DownloadUrl) { UseShellExecute = true });
    }

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset> Assets);
    private sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}
