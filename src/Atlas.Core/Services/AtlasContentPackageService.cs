using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed record AtlasContentPackageInfo(
    string ContentVersion,
    string MinimumApplicationVersion,
    DateTimeOffset CreatedUtc,
    AtlasContentModules Modules,
    int FurnitureCount,
    int SearchRuleCount,
    int TagCount);

public sealed class AtlasContentPackageService
{
    private const string ManifestEntry = "manifest.json";
    private const string SignatureEntry = "signature.bin";
    private const string CatalogEntry = "payload/catalog.atlas.json";
    private const string DictionaryEntry = "payload/search-dictionary.atlas.json";
    private const string TaxonomyEntry = "payload/component-taxonomy.json";
    private const string ReferenceDataEntry = "payload/reference-data.atlas.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _sharedRoot;
    private readonly string _publicKeyPem;

    public AtlasContentPackageService(string sharedRoot, string? publicKeyPem = null)
    {
        _sharedRoot = sharedRoot;
        _publicKeyPem = string.IsNullOrWhiteSpace(publicKeyPem) ? AtlasLicenseService.DefaultPublicKeyPem : publicKeyPem;
    }

    public string OfficialRoot => Path.Combine(_sharedRoot, "Content", "Official");
    public string StatePath => Path.Combine(OfficialRoot, "content-state.atlas.json");

    public async Task<AtlasInstalledContentState> ReadInstalledStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StatePath)) return new();
        await using var stream = File.Open(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<AtlasInstalledContentState>(stream, JsonOptions, cancellationToken) ?? new();
    }

    public async Task<AtlasContentPackageInfo> InspectAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, false);
        var (manifest, _) = await ValidateArchiveAsync(archive, cancellationToken);
        var catalog = await ReadEntryAsync<AtlasCatalog>(archive, CatalogEntry, cancellationToken);
        var dictionary = await ReadEntryAsync<SearchDictionaryDocument>(archive, DictionaryEntry, cancellationToken);
        var taxonomy = await ReadEntryAsync<ComponentTaxonomy>(archive, TaxonomyEntry, cancellationToken);
        return new AtlasContentPackageInfo(
            manifest.ContentVersion, manifest.MinimumApplicationVersion, manifest.CreatedUtc, manifest.Modules,
            catalog?.Furniture.Count ?? 0,
            (dictionary?.Synonyms.Count ?? 0) + (dictionary?.Expressions.Count ?? 0) + (dictionary?.IgnoredTerms.Count ?? 0),
            taxonomy?.Tags.Count ?? 0);
    }

    public async Task CreateAsync(string outputPath, string contentVersion, string minimumApplicationVersion,
        AtlasCatalog catalog, SearchDictionaryDocument dictionary, ComponentTaxonomy taxonomy, AtlasReferenceData referenceData,
        string workspaceRoot, string furnitureRoot, string privateKeyPem,
        AtlasContentModules modules = AtlasContentModules.All, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("La version du contenu est obligatoire.", nameof(contentVersion));
        if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("La clé privée Idéo est absente.", nameof(privateKeyPem));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var packagedCatalog = Clone(catalog);
        packagedCatalog.Settings = new WorkspaceSettings();
        packagedCatalog.Revision = 0;
        packagedCatalog.ModifiedBy = "Idéo Solutions";
        packagedCatalog.ModifiedUtc = DateTimeOffset.UtcNow;
        foreach (var universe in packagedCatalog.UniverseDefinitions.Where(item => Path.IsPathRooted(item.ImageRelativePath)))
            universe.ImageRelativePath = Path.Combine("Images", "Universes", Path.GetFileName(universe.ImageRelativePath));
        foreach (var furniture in packagedCatalog.Furniture)
        {
            if (!string.IsNullOrWhiteSpace(furniture.SourceRelativePath)) furniture.SourceRelativePath = Path.GetFileName(furniture.SourceRelativePath);
            if (!string.IsNullOrWhiteSpace(furniture.ImageRelativePath)) furniture.ImageRelativePath = Path.GetFileName(furniture.ImageRelativePath);
        }
        var packagedDictionary = PrepareOfficialDictionary(dictionary);
        var packagedTaxonomy = Clone(taxonomy);
        var manifest = new AtlasContentManifest
        {
            ContentVersion = contentVersion.Trim(),
            MinimumApplicationVersion = string.IsNullOrWhiteSpace(minimumApplicationVersion) ? "0.18.0" : minimumApplicationVersion.Trim(),
            Modules = modules
        };

        if (modules.HasFlag(AtlasContentModules.FurnitureAssets)) ValidateFurnitureAssets(catalog, furnitureRoot);

        await using var output = File.Create(outputPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        if (modules.HasFlag(AtlasContentModules.Catalog))
            await AddJsonEntryAsync(archive, CatalogEntry, packagedCatalog, manifest, cancellationToken);
        if (modules.HasFlag(AtlasContentModules.SearchDictionary))
            await AddJsonEntryAsync(archive, DictionaryEntry, packagedDictionary, manifest, cancellationToken);
        if (modules.HasFlag(AtlasContentModules.Taxonomy))
            await AddJsonEntryAsync(archive, TaxonomyEntry, packagedTaxonomy, manifest, cancellationToken);
        if (modules.HasFlag(AtlasContentModules.ReferenceData))
            await AddJsonEntryAsync(archive, ReferenceDataEntry, referenceData, manifest, cancellationToken);
        if (modules.HasFlag(AtlasContentModules.WorkspaceAssets))
            await AddWorkspaceAssetsAsync(archive, catalog, packagedCatalog, workspaceRoot, manifest, cancellationToken);
        if (modules.HasFlag(AtlasContentModules.FurnitureAssets))
            await AddFurnitureAssetsAsync(archive, catalog, packagedCatalog, furnitureRoot, manifest, cancellationToken);

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var manifestZipEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        await using (var stream = manifestZipEntry.Open()) await stream.WriteAsync(manifestBytes, cancellationToken);
        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        var signature = signer.SignData(manifestBytes, HashAlgorithmName.SHA256);
        var signatureZipEntry = archive.CreateEntry(SignatureEntry, CompressionLevel.NoCompression);
        await using (var stream = signatureZipEntry.Open()) await stream.WriteAsync(signature, cancellationToken);
    }

    public async Task<AtlasInstalledContentState> InstallAsync(string packagePath, string furnitureRoot,
        Version currentApplicationVersion, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(_sharedRoot, "Backups", "Content"));
        Directory.CreateDirectory(OfficialRoot);
        Directory.CreateDirectory(Path.Combine(_sharedRoot, "Locks"));
        await using var installLock = await AcquireInstallLockAsync(cancellationToken);
        var staging = Path.Combine(_sharedRoot, "Content", $".staging-{Guid.NewGuid():N}");
        var rollback = Path.Combine(_sharedRoot, "Content", $".rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(rollback);
        var createdTargets = new List<string>();
        var backedUpTargets = new List<(string Original, string Backup)>();
        var officialRollback = Path.Combine(rollback, "official");
        var officialTouched = false;

        try
        {
            AtlasContentManifest manifest;
            await using (var input = File.OpenRead(packagePath))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read, false))
            {
                (manifest, _) = await ValidateArchiveAsync(archive, cancellationToken);
                if (!Version.TryParse(manifest.MinimumApplicationVersion, out var minimum))
                    throw new InvalidDataException("La version minimale du pack est invalide.");
                if (currentApplicationVersion < minimum)
                    throw new InvalidOperationException($"Ce contenu nécessite Atlas {minimum.ToString(3)} ou une version plus récente.");

                foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith("payload/", StringComparison.OrdinalIgnoreCase)))
                    await ExtractSafeAsync(entry, staging, entry.FullName["payload/".Length..], cancellationToken);

                foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith("assets/workspace/", StringComparison.OrdinalIgnoreCase)))
                {
                    var relative = entry.FullName["assets/workspace/".Length..];
                    await InstallAssetAsync(entry, _sharedRoot, relative, rollback, createdTargets, backedUpTargets, cancellationToken);
                }
                foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith("assets/furniture/", StringComparison.OrdinalIgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(furnitureRoot)) throw new InvalidOperationException("Le dossier des meubles Atlas doit être configuré avant d’installer ce pack.");
                    var relative = entry.FullName["assets/furniture/".Length..];
                    await InstallAssetAsync(entry, furnitureRoot, relative, rollback, createdTargets, backedUpTargets, cancellationToken);
                }
            }

            var backupFolder = Path.Combine(_sharedRoot, "Backups", "Content", $"{DateTime.Now:yyyyMMdd-HHmmss}-{SafeName(manifest.ContentVersion)}");
            if (Directory.Exists(OfficialRoot)) CopyDirectory(OfficialRoot, backupFolder);
            if (Directory.Exists(OfficialRoot)) CopyDirectory(OfficialRoot, officialRollback);
            officialTouched = true;
            foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, source);
                var target = SafeTarget(OfficialRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + $".{Guid.NewGuid():N}.tmp";
                File.Copy(source, temporary, true);
                File.Move(temporary, target, true);
            }

            var state = new AtlasInstalledContentState
            {
                ContentVersion = manifest.ContentVersion,
                PackageId = manifest.PackageId,
                InstalledUtc = DateTimeOffset.UtcNow,
                Modules = manifest.Modules
            };
            await WriteAtomicAsync(StatePath, state, cancellationToken);
            Directory.Delete(staging, true);
            Directory.Delete(rollback, true);
            return state;
        }
        catch
        {
            if (officialTouched)
            {
                if (Directory.Exists(OfficialRoot)) Directory.Delete(OfficialRoot, true);
                if (Directory.Exists(officialRollback)) CopyDirectory(officialRollback, OfficialRoot);
            }
            foreach (var target in createdTargets.Where(File.Exists)) File.Delete(target);
            foreach (var (original, backup) in backedUpTargets.Where(item => File.Exists(item.Backup)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Copy(backup, original, true);
            }
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true);
            throw;
        }
    }

    private async Task<(AtlasContentManifest Manifest, byte[] ManifestBytes)> ValidateArchiveAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifestEntry = archive.GetEntry(ManifestEntry) ?? throw new InvalidDataException("Le manifeste du pack est absent.");
        var signatureEntry = archive.GetEntry(SignatureEntry) ?? throw new InvalidDataException("La signature du pack est absente.");
        var manifestBytes = await ReadAllBytesAsync(manifestEntry, cancellationToken);
        var signature = await ReadAllBytesAsync(signatureEntry, cancellationToken);
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(_publicKeyPem);
        if (!verifier.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256))
            throw new CryptographicException("La signature Idéo du pack est invalide.");
        var manifest = JsonSerializer.Deserialize<AtlasContentManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("Le manifeste du pack est illisible.");
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.ContentVersion))
            throw new InvalidDataException("Le format du pack n’est pas pris en charge.");
        var duplicates = archive.Entries.GroupBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null) throw new InvalidDataException($"Le pack contient plusieurs fichiers nommés {duplicates.Key}.");
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.Equals(ManifestEntry, StringComparison.OrdinalIgnoreCase)
                                                              && !entry.FullName.Equals(SignatureEntry, StringComparison.OrdinalIgnoreCase)))
            if (!manifest.Sha256.ContainsKey(entry.FullName)) throw new InvalidDataException($"Le fichier non signé {entry.FullName} a été ajouté au pack.");
        foreach (var (path, expected) in manifest.Sha256)
        {
            var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Le fichier {path} annoncé par le pack est absent.");
            var actual = Convert.ToHexString(SHA256.HashData(await ReadAllBytesAsync(entry, cancellationToken))).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Le contrôle d’intégrité de {path} a échoué.");
        }
        return (manifest, manifestBytes);
    }

    private static async Task AddJsonEntryAsync<T>(ZipArchive archive, string path, T value, AtlasContentManifest manifest, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await AddBytesEntryAsync(archive, path, bytes, manifest, cancellationToken);
    }

    private static async Task AddWorkspaceAssetsAsync(ZipArchive archive, AtlasCatalog sourceCatalog, AtlasCatalog packagedCatalog, string workspaceRoot,
        AtlasContentManifest manifest, CancellationToken cancellationToken)
    {
        var packagedById = packagedCatalog.UniverseDefinitions.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var universe in sourceCatalog.UniverseDefinitions.Where(item => !string.IsNullOrWhiteSpace(item.ImageRelativePath)))
        {
            if (!packagedById.TryGetValue(universe.Id, out var packaged)) continue;
            var source = ResolveSource(workspaceRoot, universe.ImageRelativePath);
            if (!File.Exists(source)) continue;
            var safeRelative = NormalizeRelative(packaged.ImageRelativePath);
            await AddFileEntryAsync(archive, $"assets/workspace/{safeRelative}", source, manifest, cancellationToken);
        }
    }

    private static async Task AddFurnitureAssetsAsync(ZipArchive archive, AtlasCatalog sourceCatalog, AtlasCatalog packagedCatalog, string furnitureRoot,
        AtlasContentManifest manifest, CancellationToken cancellationToken)
    {
        var packagedById = packagedCatalog.Furniture.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var furniture in sourceCatalog.Furniture)
        {
            if (!packagedById.TryGetValue(furniture.Id, out var packaged)) continue;
            foreach (var pair in new[]
                     {
                         (Source: furniture.SourceRelativePath, Target: packaged.SourceRelativePath),
                         (Source: furniture.ImageRelativePath, Target: packaged.ImageRelativePath)
                     }.Where(item => !string.IsNullOrWhiteSpace(item.Source) && !string.IsNullOrWhiteSpace(item.Target)))
            {
                var source = ResolveSource(furnitureRoot, pair.Source);
                if (!File.Exists(source)) continue;
                await AddFileEntryAsync(archive, $"assets/furniture/{NormalizeRelative(pair.Target)}", source, manifest, cancellationToken);
            }
        }
    }

    private static async Task AddFileEntryAsync(ZipArchive archive, string path, string source, AtlasContentManifest manifest, CancellationToken cancellationToken)
        => await AddBytesEntryAsync(archive, path, await File.ReadAllBytesAsync(source, cancellationToken), manifest, cancellationToken);

    private static async Task AddBytesEntryAsync(ZipArchive archive, string path, byte[] bytes, AtlasContentManifest manifest, CancellationToken cancellationToken)
    {
        path = path.Replace('\\', '/');
        if (manifest.Sha256.ContainsKey(path)) return;
        manifest.Sha256[path] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static async Task InstallAssetAsync(ZipArchiveEntry entry, string root, string relative, string rollbackRoot,
        ICollection<string> created, ICollection<(string Original, string Backup)> backedUp, CancellationToken cancellationToken)
    {
        var target = SafeTarget(root, relative);
        if (File.Exists(target))
        {
            var backup = SafeTarget(rollbackRoot, Guid.NewGuid().ToString("N") + Path.GetExtension(target));
            File.Copy(target, backup, true);
            backedUp.Add((target, backup));
        }
        else created.Add(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        await using var source = entry.Open();
        await using var output = File.Create(temporary);
        await source.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Close();
        File.Move(temporary, target, true);
    }

    private static async Task ExtractSafeAsync(ZipArchiveEntry entry, string root, string relative, CancellationToken cancellationToken)
    {
        var target = SafeTarget(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var input = entry.Open();
        await using var output = File.Create(target);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string SafeTarget(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Le pack contient un chemin non autorisé.");
        return target;
    }

    private static string ResolveSource(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var recorded = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        if (File.Exists(recorded)) return recorded;
        return Path.Combine(root, Path.GetFileName(path));
    }

    private static void ValidateFurnitureAssets(AtlasCatalog catalog, string furnitureRoot)
    {
        if (string.IsNullOrWhiteSpace(furnitureRoot)) throw new InvalidOperationException("Le dossier des meubles Atlas doit être configuré avant de générer un pack.");
        var missing = new List<string>();
        foreach (var furniture in catalog.Furniture.Where(item => !item.IsDemo))
        {
            foreach (var asset in new[]
                     {
                         (Label: "TOP", Path: furniture.SourceRelativePath),
                         (Label: "image", Path: furniture.ImageRelativePath)
                     }.Where(item => !string.IsNullOrWhiteSpace(item.Path)))
                if (!File.Exists(ResolveSource(furnitureRoot, asset.Path)))
                    missing.Add($"{furniture.Reference} · {asset.Label} · {asset.Path}");
        }
        if (missing.Count == 0) return;
        throw new FileNotFoundException("Le pack n’a pas été généré car des fichiers référencés sont introuvables :" + Environment.NewLine
                                        + string.Join(Environment.NewLine, missing.Take(12))
                                        + (missing.Count > 12 ? $"{Environment.NewLine}… et {missing.Count - 12} autre(s)." : string.Empty));
    }
    private static string NormalizeRelative(string value) => value.Replace('\\', '/').TrimStart('/');
    private static string SafeName(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static async Task<T?> ReadEntryAsync<T>(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return default;
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<byte[]> ReadAllBytesAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    private static SearchDictionaryDocument PrepareOfficialDictionary(SearchDictionaryDocument source)
    {
        var result = Clone(source);
        foreach (var item in result.Concepts) { item.IsOfficial = true; item.OfficialAliasesCsv = MergeAliases(item.OfficialAliasesCsv, item.CustomAliasesCsv); item.CustomAliasesCsv = string.Empty; }
        foreach (var item in result.Synonyms) item.IsOfficial = true;
        foreach (var item in result.Expressions) item.IsOfficial = true;
        foreach (var item in result.IgnoredTerms) item.IsOfficial = true;
        result.UnknownQueries.Clear();
        result.OfficialVersion++;
        return result;
    }

    private static string MergeAliases(params string[] values) => string.Join(", ", values.SelectMany(value =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
    }

    private async Task<FileStream> AcquireInstallLockAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_sharedRoot, "Locks", "content.install.lock");
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync($"{Environment.UserName}@{Environment.MachineName} {DateTimeOffset.Now:O}");
                await writer.FlushAsync(cancellationToken);
                stream.Position = 0;
                return stream;
            }
            catch (IOException) when (attempt < 39) { await Task.Delay(250, cancellationToken); }
        }
        throw new IOException("Une mise à jour des contenus Atlas est déjà en cours sur un autre poste.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
