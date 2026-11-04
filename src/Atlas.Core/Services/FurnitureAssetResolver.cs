using Atlas.Core.Models;

namespace Atlas.Core.Services;

public static class FurnitureAssetResolver
{
    public static string ResolveImage(FurnitureRecord furniture, params string[] roots)
    {
        var candidates = new List<string>();
        AddRecordedCandidates(candidates, furniture.ImageRelativePath, roots);
        var imageName = FileName(furniture.ImageRelativePath);
        foreach (var root in ValidRoots(roots))
        {
            if (!string.IsNullOrWhiteSpace(imageName)) candidates.Add(Path.Combine(root, imageName));
            if (!string.IsNullOrWhiteSpace(furniture.Reference)) candidates.Add(Path.Combine(root, $"{furniture.Reference}.top.png"));
            var topName = FileName(furniture.SourceRelativePath);
            if (!string.IsNullOrWhiteSpace(topName)) candidates.Add(Path.Combine(root, topName + ".png"));
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault() ?? string.Empty;
    }

    public static string ResolveFile(string recordedPath, params string[] roots)
    {
        var candidates = new List<string>();
        AddRecordedCandidates(candidates, recordedPath, roots);
        var name = FileName(recordedPath);
        if (!string.IsNullOrWhiteSpace(name))
            candidates.AddRange(ValidRoots(roots).Select(root => Path.Combine(root, name)));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault() ?? string.Empty;
    }

    private static void AddRecordedCandidates(ICollection<string> candidates, string path, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Path.IsPathRooted(path)) candidates.Add(path);
        else foreach (var root in ValidRoots(roots)) candidates.Add(Path.Combine(root, path));
    }

    private static IEnumerable<string> ValidRoots(IEnumerable<string> roots) => roots
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string FileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var normalized = path.Replace('\\', '/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }
}
