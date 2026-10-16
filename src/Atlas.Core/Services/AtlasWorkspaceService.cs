using System.Security.Cryptography;
using System.Text.Json;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public static class AtlasWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string ManifestPath(string root) => Path.Combine(root, "Configuration", "workspace.atlas.json");

    public static bool IsWorkspace(string root) => !string.IsNullOrWhiteSpace(root) && File.Exists(ManifestPath(root));

    public static async Task<AtlasWorkspaceManifest> EnsureAsync(string root, string? displayName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("L’emplacement de l’espace Atlas est obligatoire.", nameof(root));
        Directory.CreateDirectory(Path.Combine(root, "Configuration"));
        var path = ManifestPath(root);
        if (File.Exists(path))
        {
            await using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await JsonSerializer.DeserializeAsync<AtlasWorkspaceManifest>(input, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("L’identifiant de l’espace Atlas est illisible.");
        }

        var manifest = new AtlasWorkspaceManifest
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? new DirectoryInfo(root).Name : displayName.Trim()
        };
        await using var output = File.Create(path);
        await JsonSerializer.SerializeAsync(output, manifest, JsonOptions, cancellationToken);
        return manifest;
    }

    public static async Task CopyAndVerifyAsync(string sourceRoot, string destinationRoot, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!IsWorkspace(source)) throw new InvalidDataException("Le dossier source n’est pas un espace Atlas reconnu.");
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Le nouvel emplacement est identique à l’emplacement actuel.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Le dossier de destination doit être vide.");

        Directory.CreateDirectory(destination);
        var sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Locks{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, sourceFile);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourceFile, target, true);
        }

        foreach (var sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            if (!File.Exists(target) || new FileInfo(sourceFile).Length != new FileInfo(target).Length)
                throw new IOException($"La vérification de « {Path.GetFileName(sourceFile)} » a échoué.");
            if (new FileInfo(sourceFile).Length <= 16 * 1024 * 1024)
            {
                await using var left = File.OpenRead(sourceFile);
                await using var right = File.OpenRead(target);
                var leftHash = await SHA256.HashDataAsync(left, cancellationToken);
                var rightHash = await SHA256.HashDataAsync(right, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(leftHash, rightHash))
                    throw new IOException($"Le contrôle d’intégrité de « {Path.GetFileName(sourceFile)} » a échoué.");
            }
        }

        if (!IsWorkspace(destination)) throw new InvalidDataException("La copie ne contient pas l’identifiant de l’espace Atlas.");
    }
}
