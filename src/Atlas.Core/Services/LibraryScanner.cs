using System.Security.Cryptography;
using System.Text;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed class LibraryScanner
{
    public Task<LibraryScanResult> ScanAsync(string root, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(root, cancellationToken), cancellationToken);

    private static LibraryScanResult Scan(string root, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Le chemin de la bibliothèque n’est pas renseigné.", nameof(root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Bibliothèque introuvable : {root}");

        var warnings = new List<string>();
        var result = new List<ScannedComponent>();
        foreach (var path in EnumerateTopFiles(root, warnings, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var technicalName = Path.GetFileNameWithoutExtension(path);
            var compliant = ComponentNameParser.TryParse(technicalName, out var parsed);
            var preview = File.Exists(path + ".png") ? Path.GetRelativePath(root, path + ".png") : string.Empty;
            result.Add(new ScannedComponent(
                StableId(relative),
                relative,
                preview,
                parts.Length > 1 ? parts[0] : "Racine",
                parts.Length > 2 ? parts[1] : "Racine",
                technicalName,
                parsed,
                compliant));
        }

        return new LibraryScanResult(
            result.OrderBy(item => item.Library, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Family, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TechnicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            warnings);
    }

    private static IEnumerable<string> EnumerateTopFiles(string root, ICollection<string> warnings, CancellationToken token)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(directory)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(file => Path.GetExtension(file).Equals(".top", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Dossier inaccessible : {directory} ({exception.Message})");
                files = [];
            }
            foreach (var file in files) yield return file;

            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(directory).ToArray(); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Sous-dossiers inaccessibles : {directory} ({exception.Message})");
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                    else warnings.Add($"Lien de dossier ignoré : {child}");
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    warnings.Add($"Dossier ignoré : {child} ({exception.Message})");
                }
            }
        }
    }

    private static string StableId(string relativePath)
    {
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24].ToLowerInvariant();
    }
}
