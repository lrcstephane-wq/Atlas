using System.ComponentModel;
using System.Runtime.CompilerServices;

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
    public RecordStatus Status { get => _status; set => Set(ref _status, value); }
    public string ForcedValidationReason { get => _forcedValidationReason; set => Set(ref _forcedValidationReason, value); }
    public List<string> ComponentIds { get; set; } = [];
    public string ValidatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedUtc { get; set; }
}

public sealed class AtlasCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ModifiedBy { get; set; } = string.Empty;
    public WorkspaceSettings Settings { get; set; } = new();
    public List<ComponentRecord> Components { get; set; } = [];
    public List<FurnitureRecord> Furniture { get; set; } = [];
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
