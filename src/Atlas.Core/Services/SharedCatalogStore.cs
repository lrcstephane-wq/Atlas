using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed class CatalogConcurrencyException(string message) : IOException(message);

public sealed class SharedCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SharedRoot { get; private set; }
    public string CatalogPath => Path.Combine(SharedRoot, "Data", "catalog.atlas.json");

    public SharedCatalogStore(string sharedRoot) => SharedRoot = sharedRoot;

    public void ChangeRoot(string sharedRoot) => SharedRoot = sharedRoot;

    public async Task<AtlasCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureFolders();
        if (!File.Exists(CatalogPath)) return DemoCatalogFactory.Create();
        await using var stream = File.Open(CatalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<AtlasCatalog>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Le catalogue partagé est vide ou illisible.");
    }

    public async Task SaveAsync(AtlasCatalog catalog, long expectedRevision, string editor, CancellationToken cancellationToken = default)
    {
        EnsureFolders();
        await using var writeLock = await AcquireLockAsync(cancellationToken);
        var currentRevision = await ReadRevisionAsync(cancellationToken);
        if (File.Exists(CatalogPath) && currentRevision != expectedRevision)
            throw new CatalogConcurrencyException("Le catalogue a été modifié par un autre poste. Rechargez-le avant d’enregistrer vos changements.");

        if (File.Exists(CatalogPath))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(CatalogPath, Path.Combine(SharedRoot, "Backups", $"catalog-{stamp}-r{currentRevision}.atlas.json"), false);
            TrimBackups();
        }

        catalog.Revision = currentRevision + 1;
        catalog.ModifiedUtc = DateTimeOffset.UtcNow;
        catalog.ModifiedBy = string.IsNullOrWhiteSpace(editor) ? Environment.UserName : editor;
        var temporary = CatalogPath + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
        File.Move(temporary, CatalogPath, true);
        await File.AppendAllTextAsync(Path.Combine(SharedRoot, "Logs", $"atlas-{DateTime.Now:yyyyMM}.log"),
            $"{DateTimeOffset.Now:O}\t{catalog.ModifiedBy}\t{Environment.MachineName}\tSAVE\tr{catalog.Revision}{Environment.NewLine}", cancellationToken);
    }

    public static string LocalBootstrapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ideo Solutions", "Atlas", "bootstrap.json");

    public static async Task<LocalBootstrap> LoadBootstrapAsync()
    {
        if (!File.Exists(LocalBootstrapPath)) return new LocalBootstrap();
        await using var stream = File.OpenRead(LocalBootstrapPath);
        return await JsonSerializer.DeserializeAsync<LocalBootstrap>(stream, JsonOptions) ?? new LocalBootstrap();
    }

    public static async Task SaveBootstrapAsync(LocalBootstrap bootstrap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalBootstrapPath)!);
        await using var stream = File.Create(LocalBootstrapPath);
        await JsonSerializer.SerializeAsync(stream, bootstrap, JsonOptions);
    }

    private void EnsureFolders()
    {
        if (string.IsNullOrWhiteSpace(SharedRoot)) throw new InvalidOperationException("Le dossier partagé Atlas n’est pas renseigné.");
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Configuration"));
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Data"));
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Backups"));
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Logs"));
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Images"));
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Locks"));
    }

    private async Task<long> ReadRevisionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CatalogPath)) return 0;
        await using var stream = File.Open(CatalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("Revision", out var revision) ? revision.GetInt64() : 0;
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(SharedRoot, "Locks", "catalog.write.lock");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync($"{Environment.UserName}@{Environment.MachineName} {DateTimeOffset.Now:O}");
                await writer.FlushAsync(cancellationToken);
                stream.Position = 0;
                return stream;
            }
            catch (IOException)
            {
                await Task.Delay(150, cancellationToken);
            }
        }
        throw new IOException("Le catalogue est déjà en cours d’enregistrement sur un autre poste. Réessayez dans quelques secondes.");
    }

    private void TrimBackups()
    {
        foreach (var oldFile in Directory.EnumerateFiles(Path.Combine(SharedRoot, "Backups"), "catalog-*.atlas.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc).Skip(30)) File.Delete(oldFile);
    }
}

public static class DemoCatalogFactory
{
    public static AtlasCatalog Create()
    {
        var family = new FurnitureFamilyRecord
        {
            Id = "FAM-BAS-01", Name = "Meubles bas", Description = "Caissons bas modulaires pour l’agencement.",
            TypeMeuble = "Meuble bas", Forme = "Droit", Universes = ["Cuisine", "Buanderie"], CreatedBy = "Démonstration"
        };
        var componentA = new ComponentRecord
        {
            Id = "demo-tiroir", IsDemo = true, DisplayName = "Tiroir AvanTech YOU", Function = "Coulissant",
            Description = "Exemple de fiche composant issue de la bibliothèque.", LibraryName = "Hettich", FamilyName = "Tiroirs",
            TechnicalName = "TIROIR#V=AVANTECH#I=470#R=YOU", TypeCode = "TIROIR", VariantCode = "AVANTECH", IndexCode = "470", RangeCode = "YOU",
            IsNameCompliant = true, CapabilitiesCsv = "coulissant, extraction totale, amorti", CompatibilityCsv = "caisson bas, colonne", Status = RecordStatus.Publiee
        };
        var componentB = new ComponentRecord
        {
            Id = "demo-assemblage", IsDemo = true, DisplayName = "Assemblage Confirmat", Function = "Assemblage",
            Description = "Exemple d’assemblage paramétrique.", LibraryName = "_ideo_base", FamilyName = "Assemblages",
            TechnicalName = "ASSEMBLAGE#V=CONFIRMAT#I=001", TypeCode = "ASSEMBLAGE", VariantCode = "CONFIRMAT", IndexCode = "001",
            IsNameCompliant = true, CapabilitiesCsv = "assemblage démontable", CompatibilityCsv = "panneau 19 mm", Status = RecordStatus.Publiee
        };
        return new AtlasCatalog
        {
            Settings = new WorkspaceSettings(),
            Components = [componentA, componentB],
            Furniture =
            [
                new FurnitureRecord
                {
                    Id = "demo-meuble-bas", IsDemo = true, Reference = "MB-001", DisplayName = "Meuble bas 2 tiroirs", Family = "Meubles bas",
                    FamilyId = family.Id, Description = "Meuble de démonstration composé de fiches composants.", UseCasesCsv = "cuisine, rangement",
                    Universes = ["Cuisine", "Buanderie"], TypeMeuble = "Meuble bas", UsageSpecifique = "Rangement", Forme = "Droit",
                    PrincipleConstruction = "Montant filant", PositionDos = "Rainuré", TypeAssemblage = "Tourillons + vis",
                    Tiroir = true, NombreTiroirs = 2, TypologieTiroir = "Applique", SensMontage = "Tous les sens prévus", Status = RecordStatus.Publiee,
                    ComponentIds = [componentA.Id, componentB.Id]
                }
            ],
            FurnitureFamilies = [family]
        };
    }
}
