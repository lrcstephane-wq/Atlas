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
}

public static class FurnitureCatalogWindowStateStore
{
    private static string StatePath => Path.Combine(Path.GetDirectoryName(SharedCatalogStore.LocalBootstrapPath)!, "catalogue-forge.window.json");

    public static FurnitureCatalogWindowState? Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<FurnitureCatalogWindowState>(File.ReadAllText(StatePath))
                : null;
        }
        catch { return null; }
    }

    public static void Save(FurnitureCatalogWindowState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, StatePath, true);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
