using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.Core.Services;

public enum AtlasDeploymentMode
{
    Production,
    Development
}

public sealed class AtlasDeploymentPolicy
{
    public int SchemaVersion { get; set; } = 1;
    public AtlasDeploymentMode Mode { get; set; } = AtlasDeploymentMode.Production;

    [JsonIgnore]
    public bool AllowsAutomaticApplicationUpdates => Mode == AtlasDeploymentMode.Development;
}

public static class AtlasDeploymentPolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string PolicyPath => Path.Combine(AtlasPaths.ConfigurationRoot, "deployment.json");

    public static AtlasDeploymentPolicy Load(string? path = null)
    {
        path ??= PolicyPath;
        try
        {
            if (!File.Exists(path)) return new AtlasDeploymentPolicy();
            var policy = JsonSerializer.Deserialize<AtlasDeploymentPolicy>(File.ReadAllText(path), JsonOptions);
            return policy ?? new AtlasDeploymentPolicy();
        }
        catch (JsonException)
        {
            return new AtlasDeploymentPolicy();
        }
        catch (IOException)
        {
            return new AtlasDeploymentPolicy();
        }
        catch (UnauthorizedAccessException)
        {
            return new AtlasDeploymentPolicy();
        }
    }

    public static void Save(AtlasDeploymentPolicy policy, string? path = null)
    {
        path ??= PolicyPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(policy, JsonOptions));
        File.Move(temporary, path, true);
    }
}
