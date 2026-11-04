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
    public string LegacyCatalogPath => Path.Combine(SharedRoot, "Data", "catalog.atlas.json");
    public string CatalogPath => Path.Combine(SharedRoot, "Data", "catalog-site.atlas.json");
    public string OfficialCatalogPath => Path.Combine(SharedRoot, "Content", "Official", "catalog.atlas.json");

    public SharedCatalogStore(string sharedRoot) => SharedRoot = sharedRoot;

    public void ChangeRoot(string sharedRoot) => SharedRoot = sharedRoot;

    public async Task<AtlasCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureFolders();
        await AtlasWorkspaceService.EnsureAsync(SharedRoot, cancellationToken: cancellationToken);
        await MigrateLegacyAsync(cancellationToken);
        var official = await ReadAsync<AtlasCatalog>(OfficialCatalogPath, cancellationToken) ?? DemoCatalogFactory.Create();
        var overlay = await ReadAsync<CatalogSiteOverlay>(CatalogPath, cancellationToken) ?? new CatalogSiteOverlay();
        return Compose(official, overlay);
    }

    public async Task SaveAsync(AtlasCatalog catalog, long expectedRevision, string editor, CancellationToken cancellationToken = default)
    {
        EnsureFolders();
        await using var writeLock = await AcquireLockAsync(cancellationToken);
        await MigrateLegacyAsync(cancellationToken);
        var currentRevision = await ReadRevisionAsync(cancellationToken);
        if (File.Exists(CatalogPath) && currentRevision != expectedRevision)
            throw new CatalogConcurrencyException("Le catalogue a été modifié par un autre poste. Rechargez-le avant d’enregistrer vos changements.");

        if (File.Exists(CatalogPath))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(CatalogPath, Path.Combine(SharedRoot, "Backups", $"catalog-{stamp}-r{currentRevision}.atlas.json"), false);
            TrimBackups();
        }

        var official = await ReadAsync<AtlasCatalog>(OfficialCatalogPath, cancellationToken) ?? DemoCatalogFactory.Create();
        var overlay = BuildOverlay(official, catalog);
        overlay.Revision = currentRevision + 1;
        overlay.ModifiedUtc = DateTimeOffset.UtcNow;
        overlay.ModifiedBy = string.IsNullOrWhiteSpace(editor) ? Environment.UserName : editor;
        var temporary = CatalogPath + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, overlay, JsonOptions, cancellationToken);
        File.Move(temporary, CatalogPath, true);
        catalog.Revision = overlay.Revision;
        catalog.ModifiedUtc = overlay.ModifiedUtc;
        catalog.ModifiedBy = overlay.ModifiedBy;
        await File.AppendAllTextAsync(Path.Combine(SharedRoot, "Logs", $"atlas-{DateTime.Now:yyyyMM}.log"),
            $"{DateTimeOffset.Now:O}\t{catalog.ModifiedBy}\t{Environment.MachineName}\tSAVE\tr{catalog.Revision}{Environment.NewLine}", cancellationToken);
    }

    public static string LocalBootstrapPath => AtlasPaths.BootstrapPath;

    public static async Task<LocalBootstrap> LoadBootstrapAsync()
    {
        var path = LocalBootstrapPath;
        if (!File.Exists(path)) return new LocalBootstrap { SharedRoot = AtlasPaths.DefaultWorkspaceRoot };
        await using var stream = File.OpenRead(path);
        var bootstrap = await JsonSerializer.DeserializeAsync<LocalBootstrap>(stream, JsonOptions) ?? new LocalBootstrap();
        if (string.IsNullOrWhiteSpace(bootstrap.SharedRoot)) bootstrap.SharedRoot = AtlasPaths.DefaultWorkspaceRoot;
        return bootstrap;
    }

    public static async Task SaveBootstrapAsync(LocalBootstrap bootstrap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalBootstrapPath)!);
        var temporary = LocalBootstrapPath + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, bootstrap, JsonOptions);
        File.Move(temporary, LocalBootstrapPath, true);
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
        Directory.CreateDirectory(Path.Combine(SharedRoot, "Content", "Official"));
    }

    private async Task<long> ReadRevisionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CatalogPath)) return 0;
        await using var stream = File.Open(CatalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("Revision", out var revision) ? revision.GetInt64() : 0;
    }

    private async Task MigrateLegacyAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(OfficialCatalogPath)) return;
        AtlasCatalog official;
        if (File.Exists(LegacyCatalogPath))
        {
            official = await ReadAsync<AtlasCatalog>(LegacyCatalogPath, cancellationToken)
                ?? throw new InvalidDataException("Le catalogue historique est vide ou illisible.");
            var backup = Path.Combine(SharedRoot, "Backups", $"catalog-before-content-layers-{DateTime.Now:yyyyMMdd-HHmmss}.atlas.json");
            File.Copy(LegacyCatalogPath, backup, false);
        }
        else official = DemoCatalogFactory.Create();

        Directory.CreateDirectory(Path.GetDirectoryName(OfficialCatalogPath)!);
        await WriteAtomicAsync(OfficialCatalogPath, official, cancellationToken);
        if (!File.Exists(CatalogPath))
        {
            await WriteAtomicAsync(CatalogPath, new CatalogSiteOverlay
            {
                Revision = official.Revision,
                ModifiedUtc = official.ModifiedUtc,
                ModifiedBy = official.ModifiedBy,
                Settings = official.Settings ?? new()
            }, cancellationToken);
        }
    }

    private static AtlasCatalog Compose(AtlasCatalog official, CatalogSiteOverlay overlay)
    {
        var result = Clone(official);
        result.Settings = Clone(overlay.Settings ?? new());
        result.Components = Merge(official.Components, overlay.Components, overlay.RemovedComponentIds, item => item.Id);
        result.CapabilityTags = Merge(official.CapabilityTags, overlay.CapabilityTags, overlay.RemovedCapabilityTagIds, item => item.Id);
        result.Furniture = Merge(official.Furniture, overlay.Furniture, overlay.RemovedFurnitureIds, item => item.Id);
        result.FurnitureFamilies = Merge(official.FurnitureFamilies, overlay.FurnitureFamilies, overlay.RemovedFurnitureFamilyIds, item => item.Id);
        result.UniverseDefinitions = Merge(official.UniverseDefinitions, overlay.UniverseDefinitions, overlay.RemovedUniverseIds, item => item.Id);
        result.FurnitureTypes = MergeStrings(official.FurnitureTypes, overlay.AddedFurnitureTypes, overlay.RemovedFurnitureTypes);
        result.FurnitureUsages = MergeStrings(official.FurnitureUsages, overlay.AddedFurnitureUsages, overlay.RemovedFurnitureUsages);
        result.Universes = result.UniverseDefinitions.Where(item => item.IsActive).OrderBy(item => item.SortOrder).Select(item => item.Name).ToList();
        result.Revision = overlay.Revision;
        result.ModifiedUtc = overlay.ModifiedUtc;
        result.ModifiedBy = overlay.ModifiedBy;
        return result;
    }

    private static CatalogSiteOverlay BuildOverlay(AtlasCatalog official, AtlasCatalog current)
    {
        var overlay = new CatalogSiteOverlay
        {
            Settings = Clone(current.Settings ?? new()),
            Components = Differences(official.Components, current.Components, item => item.Id),
            RemovedComponentIds = Removed(official.Components, current.Components, item => item.Id),
            CapabilityTags = Differences(official.CapabilityTags, current.CapabilityTags, item => item.Id),
            RemovedCapabilityTagIds = Removed(official.CapabilityTags, current.CapabilityTags, item => item.Id),
            Furniture = Differences(official.Furniture, current.Furniture, item => item.Id),
            RemovedFurnitureIds = Removed(official.Furniture, current.Furniture, item => item.Id),
            FurnitureFamilies = Differences(official.FurnitureFamilies, current.FurnitureFamilies, item => item.Id),
            RemovedFurnitureFamilyIds = Removed(official.FurnitureFamilies, current.FurnitureFamilies, item => item.Id),
            UniverseDefinitions = Differences(official.UniverseDefinitions, current.UniverseDefinitions, item => item.Id),
            RemovedUniverseIds = Removed(official.UniverseDefinitions, current.UniverseDefinitions, item => item.Id),
            AddedFurnitureTypes = current.FurnitureTypes.Except(official.FurnitureTypes, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedFurnitureTypes = official.FurnitureTypes.Except(current.FurnitureTypes, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedFurnitureUsages = current.FurnitureUsages.Except(official.FurnitureUsages, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedFurnitureUsages = official.FurnitureUsages.Except(current.FurnitureUsages, StringComparer.OrdinalIgnoreCase).ToList()
        };
        return overlay;
    }

    private static List<T> Merge<T>(IEnumerable<T>? official, IEnumerable<T>? changed, IEnumerable<string>? removed, Func<T, string> key)
    {
        var blocked = (removed ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = (changed ?? []).Where(item => !string.IsNullOrWhiteSpace(key(item)))
            .GroupBy(key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var result = new List<T>();
        foreach (var item in official ?? [])
        {
            var id = key(item);
            if (blocked.Contains(id)) continue;
            result.Add(Clone(overrides.Remove(id, out var replacement) ? replacement : item));
        }
        result.AddRange(overrides.Values.Select(item => Clone(item)));
        return result;
    }

    private static List<T> Differences<T>(IEnumerable<T>? official, IEnumerable<T>? current, Func<T, string> key)
    {
        var baseline = (official ?? []).ToDictionary(key, item => JsonValue(item), StringComparer.OrdinalIgnoreCase);
        return (current ?? []).Where(item => !baseline.TryGetValue(key(item), out var json) || json != JsonValue(item)).Select(item => Clone(item)).ToList();
    }

    private static List<string> Removed<T>(IEnumerable<T>? official, IEnumerable<T>? current, Func<T, string> key)
    {
        var present = (current ?? []).Select(key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (official ?? []).Select(key).Where(id => !present.Contains(id)).ToList();
    }

    private static List<string> MergeStrings(IEnumerable<string>? official, IEnumerable<string>? added, IEnumerable<string>? removed)
    {
        var blocked = (removed ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (official ?? []).Concat(added ?? []).Where(value => !string.IsNullOrWhiteSpace(value) && !blocked.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string JsonValue<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
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
            UniverseDefinitions =
            [
                new CatalogUniverseRecord { Id = "univers-cuisine", Name = "Cuisine", Description = "Préparation, cuisson et rangement", SortOrder = 0 },
                new CatalogUniverseRecord { Id = "univers-buanderie", Name = "Buanderie", Description = "Entretien et rangements techniques", SortOrder = 1 }
            ],
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
