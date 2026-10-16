namespace Atlas.Core.Services;

public static class AtlasPaths
{
    public const string InstallationRoot = @"C:\Ideo_Atlas";
    public static string ApplicationRoot => Path.Combine(InstallationRoot, "Application");
    public static string ConfigurationRoot => Path.Combine(InstallationRoot, "Configuration");
    public static string CacheRoot => Path.Combine(InstallationRoot, "Cache");
    public static string LogsRoot => Path.Combine(InstallationRoot, "Logs");
    public static string UpdatesRoot => Path.Combine(InstallationRoot, "MisesAJour");
    public static string DefaultWorkspaceRoot => Path.Combine(InstallationRoot, "Donnees");
    public static string BootstrapPath => Path.Combine(ConfigurationRoot, "poste.json");
    public static string HorizonPreferencesRoot => Path.Combine(ConfigurationRoot, "Horizon");
    public static string LicenseRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Ideo Solutions", "Atlas");
    public static string LicenseFilePath => Path.Combine(LicenseRoot, "Licence.fautpastoucher");

    public static string LegacyBootstrapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions", "Atlas", "bootstrap.json");

    public static string LegacyLicenseFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions", "Atlas", "Licence.fautpastoucher");
}
