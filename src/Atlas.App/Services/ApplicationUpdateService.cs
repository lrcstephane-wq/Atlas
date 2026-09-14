using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.App.Services;

public sealed class ApplicationUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/lrcstephane-wq/Atlas/releases/latest";
    private static readonly string[] PreferredAssetNames = ["Atlas-update.zip", "Atlas-win-x64.zip"];
    private readonly HttpClient _httpClient = new();
    private ReleaseAsset? _availableAsset;

    public ApplicationUpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Biblideo-Atlas-Updater/0.3.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
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
        _availableAsset = PreferredAssetNames.Select(name => release.Assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(asset => asset is not null)
            ?? throw new InvalidOperationException($"La release {release.TagName} ne contient aucun pack de mise à jour compatible.");
        AvailableVersion = latest.ToString(3);
        return AvailableVersion;
    }

    public async Task<bool> DownloadAndInstallAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_availableAsset is null || AvailableVersion is null) throw new InvalidOperationException("Aucune mise à jour disponible.");
        var updater = Path.Combine(AppContext.BaseDirectory, "Atlas.Updater.exe");
        if (!File.Exists(updater)) return false;

        var updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Biblideo", "Atlas", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var archive = Path.Combine(updateDirectory, $"Atlas-{AvailableVersion}.zip");
        using (var response = await _httpClient.GetAsync(_availableAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(archive);
            var buffer = new byte[1024 * 128];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0) progress?.Invoke((int)Math.Clamp(received * 100 / total.Value, 0, 100));
            }
        }

        if (!string.IsNullOrWhiteSpace(_availableAsset.Digest) && _availableAsset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(archive);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            var expected = _availableAsset.Digest["sha256:".Length..].Trim().ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archive);
                throw new InvalidDataException("Le contrôle d’intégrité de la mise à jour a échoué.");
            }
        }

        var launch = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Atlas.exe");
        var start = new ProcessStartInfo(updater) { UseShellExecute = false };
        start.ArgumentList.Add("--pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--archive"); start.ArgumentList.Add(archive);
        start.ArgumentList.Add("--target"); start.ArgumentList.Add(AppContext.BaseDirectory);
        start.ArgumentList.Add("--launch"); start.ArgumentList.Add(launch);
        Process.Start(start);
        return true;
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
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
