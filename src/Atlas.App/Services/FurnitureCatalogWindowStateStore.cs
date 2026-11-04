using System.IO;
using System.Text.Json;
using Atlas.Core.Services;

namespace Atlas.App.Services;

public sealed class FurnitureCatalogWindowState
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 780;
    public bool IsMaximized { get; set; }
    public bool IsCardView { get; set; }
}

public static class FurnitureCatalogWindowStateStore
{
    private static string StatePath(string catalogName) => Path.Combine(
        Path.GetDirectoryName(SharedCatalogStore.LocalBootstrapPath)!,
        $"catalogue-{catalogName}.window.json");

    public static FurnitureCatalogWindowState? Load(string catalogName = "forge")
    {
        try
        {
            var path = StatePath(catalogName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<FurnitureCatalogWindowState>(File.ReadAllText(path))
                : null;
        }
        catch { return null; }
    }

    public static void Save(FurnitureCatalogWindowState state, string catalogName = "forge")
    {
        try
        {
            var path = StatePath(catalogName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
