using System.Diagnostics;
using System.IO.Compression;

static string? Value(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Biblideo", "Atlas", "Updates");
Directory.CreateDirectory(logDirectory);
var logPath = Path.Combine(logDirectory, "updater.log");

try
{
    if (!int.TryParse(Value(args, "--pid"), out var pid)) throw new ArgumentException("PID manquant.");
    var archive = Path.GetFullPath(Value(args, "--archive") ?? throw new ArgumentException("Archive manquante."));
    var target = Path.GetFullPath(Value(args, "--target") ?? throw new ArgumentException("Dossier cible manquant."));
    var launch = Path.GetFullPath(Value(args, "--launch") ?? throw new ArgumentException("Exécutable manquant."));

    if (!File.Exists(archive) || !File.Exists(Path.Combine(target, "Atlas.exe")))
        throw new InvalidOperationException("Installation Atlas non reconnue.");

    try { Process.GetProcessById(pid).WaitForExit(60_000); } catch (ArgumentException) { }

    var staging = Path.Combine(logDirectory, "staging-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(staging);
    ZipFile.ExtractToDirectory(archive, staging, true);

    foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(staging, source);
        if (relative.Equals("Atlas.Updater.exe", StringComparison.OrdinalIgnoreCase)) continue;
        var destination = Path.GetFullPath(Path.Combine(target, relative));
        if (!destination.StartsWith(target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Chemin de mise à jour invalide.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var copied = false;
        for (var attempt = 0; attempt < 8 && !copied; attempt++)
        {
            try { File.Copy(source, destination, true); copied = true; }
            catch (IOException) when (attempt < 7) { Thread.Sleep(500); }
        }
        if (!copied) throw new IOException($"Impossible de remplacer {relative}.");
    }

    Directory.Delete(staging, true);
    File.Delete(archive);
    File.AppendAllText(logPath, $"{DateTimeOffset.Now:u} Mise à jour installée.\n");
    Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true });
}
catch (Exception exception)
{
    File.AppendAllText(logPath, $"{DateTimeOffset.Now:u} Échec : {exception}\n");
    Environment.ExitCode = 1;
}

