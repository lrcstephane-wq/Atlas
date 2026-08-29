using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Atlas.Core.Models;

public enum CatalogEnvironment
{
    NonConfigure,
    SC,
    EP
}

public enum RecordStatus
{
    Brouillon,
    AControler,
    Validee,
    Retenue,
    Publiee
}

[Flags]
public enum UserPermissions
{
    None = 0,
    Read = 1,
    Edit = 2,
    Validate = 4,
    Administer = 8
}

public sealed class UserAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int PasswordIterations { get; set; } = 180_000;
    public UserPermissions Permissions { get; set; } = UserPermissions.Read;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginUtc { get; set; }

    public bool CanEdit => Permissions.HasFlag(UserPermissions.Edit) || Permissions.HasFlag(UserPermissions.Administer);
    public bool CanValidate => Permissions.HasFlag(UserPermissions.Validate) || Permissions.HasFlag(UserPermissions.Administer);
    public bool IsAdministrator => Permissions.HasFlag(UserPermissions.Administer);
}

public abstract class BindableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class WorkspaceSettings : BindableModel
{
    private CatalogEnvironment _environment;
    private string _libraryRoot = string.Empty;
    private bool _autoUpdate = true;

    public CatalogEnvironment Environment { get => _environment; set => Set(ref _environment, value); }
    public string LibraryRoot { get => _libraryRoot; set => Set(ref _libraryRoot, value); }
    public bool AutoUpdate { get => _autoUpdate; set => Set(ref _autoUpdate, value); }
}

public sealed class LocalBootstrap : BindableModel
{
    private string _sharedRoot = string.Empty;

    public string SharedRoot { get => _sharedRoot; set => Set(ref _sharedRoot, value); }
}

public sealed class ComponentRecord : BindableModel
{
    private string _displayName = string.Empty;
    private string _function = string.Empty;
    private string _description = string.Empty;
    private string _usageNotes = string.Empty;
    private string _capabilitiesCsv = string.Empty;
    private string _compatibilityCsv = string.Empty;
    private RecordStatus _status;
    private string _forcedValidationReason = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceRelativePath { get; set; } = string.Empty;
    public string PreviewRelativePath { get; set; } = string.Empty;
    public string LibraryName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TechnicalName { get; set; } = string.Empty;
    public string TypeCode { get; set; } = string.Empty;
    public string VariantCode { get; set; } = string.Empty;
    public string IndexCode { get; set; } = string.Empty;
    public string RangeCode { get; set; } = string.Empty;
    public string ConstructionCode { get; set; } = string.Empty;
    public bool IsNameCompliant { get; set; }
    public bool IsMissing { get; set; }
    public bool IsDemo { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }
    public string Function { get => _function; set => Set(ref _function, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string UsageNotes { get => _usageNotes; set => Set(ref _usageNotes, value); }
    public string CapabilitiesCsv { get => _capabilitiesCsv; set => Set(ref _capabilitiesCsv, value); }
    public string CompatibilityCsv { get => _compatibilityCsv; set => Set(ref _compatibilityCsv, value); }
    public RecordStatus Status { get => _status; set => Set(ref _status, value); }
    public string ForcedValidationReason { get => _forcedValidationReason; set => Set(ref _forcedValidationReason, value); }
    public string ValidatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedUtc { get; set; }

    public string Classification => IsNameCompliant ? $"{TypeCode} · {VariantCode} · {IndexCode}" : "Non classé";
}

public sealed class FurnitureRecord : BindableModel
{
    private string _reference = string.Empty;
    private string _displayName = string.Empty;
    private string _family = string.Empty;
    private string _description = string.Empty;
    private string _useCasesCsv = string.Empty;
    private string _principleConstruction = string.Empty;
    private string _sensMontage = string.Empty;
    private string _familyId = string.Empty;
    private string _typeMeuble = string.Empty;
    private string _usageSpecifique = string.Empty;
    private string _forme = "Droit";
    private string _positionDos = string.Empty;
    private string _typeAssemblage = string.Empty;
    private string _sourceRelativePath = string.Empty;
    private string _imageRelativePath = string.Empty;
    private bool _separationHorizontale;
    private bool _separationVerticale;
    private bool _porte;
    private bool _tiroir;
    private bool _tiroirAnglaise;
    private bool _abattant;
    private bool _relevant;
    private bool _rayon;
    private bool _penderie;
    private bool _nicheOuverte;
    private string _typologiePorte = string.Empty;
    private string _typologieTiroir = string.Empty;
    private string _typologieAbattant = string.Empty;
    private string _typologieRelevant = string.Empty;
    private int _nombrePortes;
    private int _nombreTiroirs;
    private int _nombreTiroirsAnglaise;
    private int _nombreAbattants;
    private int _nombreRelevants;
    private int _nombreRayons;
    private int _nombreNichesOuvertes;
    private RecordStatus _status;
    private string _forcedValidationReason = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsDemo { get; set; }
    public string Reference { get => _reference; set => Set(ref _reference, value); }
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }
    public string Family { get => _family; set => Set(ref _family, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string UseCasesCsv { get => _useCasesCsv; set => Set(ref _useCasesCsv, value); }
    public string PrincipleConstruction { get => _principleConstruction; set => Set(ref _principleConstruction, value); }
    public string SensMontage { get => _sensMontage; set => Set(ref _sensMontage, value); }
    public string FamilyId { get => _familyId; set => Set(ref _familyId, value); }
    public string TypeMeuble { get => _typeMeuble; set => Set(ref _typeMeuble, value); }
    public string UsageSpecifique { get => _usageSpecifique; set => Set(ref _usageSpecifique, value); }
    public string Forme { get => _forme; set => Set(ref _forme, value); }
    public string PositionDos { get => _positionDos; set => Set(ref _positionDos, value); }
    public string TypeAssemblage { get => _typeAssemblage; set => Set(ref _typeAssemblage, value); }
    public string SourceRelativePath { get => _sourceRelativePath; set => Set(ref _sourceRelativePath, value); }
    public string ImageRelativePath { get => _imageRelativePath; set => Set(ref _imageRelativePath, value); }
    public bool SeparationHorizontale { get => _separationHorizontale; set => Set(ref _separationHorizontale, value); }
    public bool SeparationVerticale { get => _separationVerticale; set => Set(ref _separationVerticale, value); }
    public bool Porte { get => _porte; set => Set(ref _porte, value); }
    public bool Tiroir { get => _tiroir; set => Set(ref _tiroir, value); }
    public bool TiroirAnglaise { get => _tiroirAnglaise; set => Set(ref _tiroirAnglaise, value); }
    public bool Abattant { get => _abattant; set => Set(ref _abattant, value); }
    public bool Relevant { get => _relevant; set => Set(ref _relevant, value); }
    public bool Rayon { get => _rayon; set => Set(ref _rayon, value); }
    public bool Penderie { get => _penderie; set => Set(ref _penderie, value); }
    public bool NicheOuverte { get => _nicheOuverte; set => Set(ref _nicheOuverte, value); }
    public string TypologiePorte { get => _typologiePorte; set => Set(ref _typologiePorte, value); }
    public string TypologieTiroir { get => _typologieTiroir; set => Set(ref _typologieTiroir, value); }
    public string TypologieAbattant { get => _typologieAbattant; set => Set(ref _typologieAbattant, value); }
    public string TypologieRelevant { get => _typologieRelevant; set => Set(ref _typologieRelevant, value); }
    public int NombrePortes { get => _nombrePortes; set => Set(ref _nombrePortes, Math.Max(0, value)); }
    public int NombreTiroirs { get => _nombreTiroirs; set => Set(ref _nombreTiroirs, Math.Max(0, value)); }
    public int NombreTiroirsAnglaise { get => _nombreTiroirsAnglaise; set => Set(ref _nombreTiroirsAnglaise, Math.Max(0, value)); }
    public int NombreAbattants { get => _nombreAbattants; set => Set(ref _nombreAbattants, Math.Max(0, value)); }
    public int NombreRelevants { get => _nombreRelevants; set => Set(ref _nombreRelevants, Math.Max(0, value)); }
    public int NombreRayons { get => _nombreRayons; set => Set(ref _nombreRayons, Math.Max(0, value)); }
    public int NombreNichesOuvertes { get => _nombreNichesOuvertes; set => Set(ref _nombreNichesOuvertes, Math.Max(0, value)); }
    public List<string> Universes { get; set; } = [];
    [JsonIgnore]
    public string UniversesCsv
    {
        get => string.Join(", ", Universes);
        set
        {
            Universes = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Raise();
        }
    }
    public RecordStatus Status { get => _status; set => Set(ref _status, value); }
    public string ForcedValidationReason { get => _forcedValidationReason; set => Set(ref _forcedValidationReason, value); }
    public List<string> ComponentIds { get; set; } = [];
    public string ValidatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedUtc { get; set; }
}

public sealed class FurnitureFamilyRecord : BindableModel
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _typeMeuble = string.Empty;
    private string _usageSpecifique = string.Empty;
    private string _forme = "Droit";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string TypeMeuble { get => _typeMeuble; set => Set(ref _typeMeuble, value); }
    public string UsageSpecifique { get => _usageSpecifique; set => Set(ref _usageSpecifique, value); }
    public string Forme { get => _forme; set => Set(ref _forme, value); }
    public List<string> Universes { get; set; } = [];
    [JsonIgnore]
    public string UniversesCsv
    {
        get => string.Join(", ", Universes);
        set
        {
            Universes = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Raise();
        }
    }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}

public sealed class AtlasCatalog
{
    public int SchemaVersion { get; set; } = 2;
    public long Revision { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ModifiedBy { get; set; } = string.Empty;
    public WorkspaceSettings Settings { get; set; } = new();
    public List<ComponentRecord> Components { get; set; } = [];
    public List<FurnitureRecord> Furniture { get; set; } = [];
    public List<FurnitureFamilyRecord> FurnitureFamilies { get; set; } = [];
    public List<string> Universes { get; set; } =
    [
        "Cuisine", "Dressing", "Salle de bain", "Bibliothèque", "Séjour", "Bureau / Tertiaire",
        "Buanderie", "Agencement commercial", "Chambre", "Hôtellerie / Hébergement", "Restaurant / Bar"
    ];
}

public sealed record ScannedComponent(
    string StableId,
    string RelativeTopPath,
    string PreviewRelativePath,
    string Library,
    string Family,
    string TechnicalName,
    ParsedComponentName Parsed,
    bool IsCompliant);

public sealed record LibraryScanResult(IReadOnlyList<ScannedComponent> Components, IReadOnlyList<string> Warnings);

public sealed record ParsedComponentName(
    string Type,
    string Variant,
    string Index,
    string Range,
    string Construction);
