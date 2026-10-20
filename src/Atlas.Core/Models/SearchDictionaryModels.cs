namespace Atlas.Core.Models;

public enum SearchConceptTarget
{
    GenericText,
    FurnitureType,
    FurnitureUsage,
    FurnitureTag,
    FurnitureForm,
    Construction,
    Assembly,
    BackPosition,
    DoorCount,
    DrawerCount,
    InnerDrawerCount,
    FlapCount,
    LiftUpCount,
    ShelfCount,
    OpenNicheCount,
    Hanging,
    HorizontalSeparation,
    VerticalSeparation
}

public enum SearchConstraintMode
{
    Contains,
    Minimum,
    Exact,
    Maximum
}

public sealed class SearchConceptRecord : BindableModel
{
    private string _label = string.Empty;
    private string _category = "Autre";
    private string _officialAliasesCsv = string.Empty;
    private string _customAliasesCsv = string.Empty;
    private string _targetValue = string.Empty;
    private SearchConceptTarget _target;
    private bool _isActive = true;
    private bool _isStrict = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Label { get => _label; set => Set(ref _label, value); }
    public string Category { get => _category; set => Set(ref _category, value); }
    public SearchConceptTarget Target
    {
        get => _target;
        set { if (Set(ref _target, value)) Raise(nameof(TargetLabel)); }
    }
    public string TargetValue { get => _targetValue; set => Set(ref _targetValue, value); }
    public string OfficialAliasesCsv { get => _officialAliasesCsv; set => Set(ref _officialAliasesCsv, value); }
    public string CustomAliasesCsv { get => _customAliasesCsv; set => Set(ref _customAliasesCsv, value); }
    public bool IsOfficial { get; set; }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsStrict { get => _isStrict; set => Set(ref _isStrict, value); }
    public string OriginLabel => IsOfficial ? "Atlas" : "Client";
    public string TargetLabel => Target switch
    {
        SearchConceptTarget.GenericText => "Texte libre",
        SearchConceptTarget.FurnitureType => "Type de meuble",
        SearchConceptTarget.FurnitureUsage => "Usage / équipement",
        SearchConceptTarget.FurnitureTag => "Tag / marque",
        SearchConceptTarget.FurnitureForm => "Forme",
        SearchConceptTarget.Construction => "Construction",
        SearchConceptTarget.Assembly => "Assemblage",
        SearchConceptTarget.BackPosition => "Position du dos",
        SearchConceptTarget.DoorCount => "Nombre de portes",
        SearchConceptTarget.DrawerCount => "Nombre de tiroirs",
        SearchConceptTarget.InnerDrawerCount => "Tiroirs à l’anglaise",
        SearchConceptTarget.FlapCount => "Nombre d’abattants",
        SearchConceptTarget.LiftUpCount => "Nombre de relevants",
        SearchConceptTarget.ShelfCount => "Nombre de rayons",
        SearchConceptTarget.OpenNicheCount => "Nombre de niches",
        SearchConceptTarget.Hanging => "Penderie",
        SearchConceptTarget.HorizontalSeparation => "Séparation horizontale",
        SearchConceptTarget.VerticalSeparation => "Séparation verticale",
        _ => Target.ToString()
    };

    public IEnumerable<string> Expressions()
    {
        yield return Label;
        foreach (var value in SplitAliases(OfficialAliasesCsv)) yield return value;
        foreach (var value in SplitAliases(CustomAliasesCsv)) yield return value;
    }

    private static IEnumerable<string> SplitAliases(string value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class SearchUnknownQueryRecord : BindableModel
{
    private int _count = 1;
    private DateTimeOffset _lastSeenUtc = DateTimeOffset.UtcNow;

    public string Query { get; set; } = string.Empty;
    public string NormalizedQuery { get; set; } = string.Empty;
    public int Count { get => _count; set => Set(ref _count, Math.Max(1, value)); }
    public DateTimeOffset LastSeenUtc
    {
        get => _lastSeenUtc;
        set { if (Set(ref _lastSeenUtc, value)) Raise(nameof(LastSeenLabel)); }
    }
    public string LastSeenLabel => LastSeenUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed class SearchDictionaryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public int OfficialVersion { get; set; }
    public List<SearchConceptRecord> Concepts { get; set; } = [];
    public List<SearchUnknownQueryRecord> UnknownQueries { get; set; } = [];
}

public sealed record SearchConstraint(
    SearchConceptTarget Target,
    string Value,
    SearchConstraintMode Mode,
    int Quantity,
    bool IsStrict,
    string ConceptLabel);

public sealed class SearchIntent
{
    public string OriginalQuery { get; init; } = string.Empty;
    public string CorrectedQuery { get; init; } = string.Empty;
    public string RemainingQuery { get; init; } = string.Empty;
    public List<SearchConstraint> Constraints { get; init; } = [];
    public List<string> RecognizedExpressions { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
