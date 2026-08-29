using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.App.Services;

public sealed class ApplicationUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/lrcstephane-wq/Atlas/releases/latest";
    private const string AssetName = "Atlas.exe";
    private readonly HttpClient _httpClient = new();
    private ReleaseAsset? _availableAsset;

    public ApplicationUpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Biblideo-Atlas-Updater/0.1");
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

    public async Task DownloadAndRestartAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_availableAsset is null) throw new InvalidOperationException("Aucune mise à jour disponible.");
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
            throw new InvalidOperationException("L’exécutable Atlas actuel est introuvable.");
        var updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ideo Solutions", "Atlas", "Updates", AvailableVersion ?? "latest");
        Directory.CreateDirectory(updateDirectory);
        var downloaded = Path.Combine(updateDirectory, AssetName);
        using var response = await _httpClient.GetAsync(_availableAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(downloaded, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
        {
            var buffer = new byte[131072];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total is > 0) progress?.Invoke((int)(received * 100 / total.Value));
            }
        }
        VerifyDigest(downloaded, _availableAsset.Digest);
        StartReplacement(currentExecutable, downloaded);
        Environment.Exit(0);
    }

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private static void VerifyDigest(string path, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return;
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(digest[7..].Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La mise à jour téléchargée a échoué au contrôle d’intégrité.");
    }

    private static void StartReplacement(string currentExecutable, string downloaded)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Atlas_Update_{Guid.NewGuid():N}.cmd");
        var processId = Environment.ProcessId;
        var script = new StringBuilder()
            .AppendLine("@echo off").AppendLine("setlocal")
            .AppendLine("for /l %%i in (1,1,45) do (")
            .AppendLine($"  tasklist /FI \"PID eq {processId}\" 2>NUL | find \"{processId}\" >NUL")
            .AppendLine("  if errorlevel 1 goto replace").AppendLine("  timeout /t 1 /nobreak >NUL").AppendLine(")")
            .AppendLine(":replace").AppendLine($"copy /y \"{downloaded}\" \"{currentExecutable}\" >NUL")
            .AppendLine("if errorlevel 1 exit /b 1").AppendLine($"start \"\" \"{currentExecutable}\"")
            .AppendLine("del \"%~f0\"").ToString();
        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"") { CreateNoWindow = true, UseShellExecute = false });
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset> Assets);
    private sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
