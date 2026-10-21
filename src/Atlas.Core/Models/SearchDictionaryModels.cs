using System.Text.Json.Serialization;

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
    VerticalSeparation,
    SpatialRelation
}

public enum SearchConstraintMode
{
    Contains,
    Minimum,
    Exact,
    Maximum,
    Exclude
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
        SearchConceptTarget.SpatialRelation => "Position demandée",
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

    public override string ToString() => Label;
}

public sealed class SearchSynonymEntry : BindableModel
{
    private string _conceptId = string.Empty;
    private string _conceptLabel = string.Empty;
    private string _term = string.Empty;
    private bool _isActive = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string ConceptId { get => _conceptId; set => Set(ref _conceptId, value); }
    [JsonIgnore]
    public string ConceptLabel { get => _conceptLabel; set => Set(ref _conceptLabel, value); }
    public string Term { get => _term; set => Set(ref _term, value); }
    public bool IsOfficial { get; set; }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public string OriginLabel => IsOfficial ? "Atlas" : "Client";
}

public sealed class SearchExpressionRecord : BindableModel
{
    private string _phrase = string.Empty;
    private string _conceptIdsCsv = string.Empty;
    private string _description = string.Empty;
    private bool _isActive = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Phrase { get => _phrase; set => Set(ref _phrase, value); }
    public string ConceptIdsCsv { get => _conceptIdsCsv; set => Set(ref _conceptIdsCsv, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public bool IsOfficial { get; set; }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public string OriginLabel => IsOfficial ? "Atlas" : "Client";
    public IEnumerable<string> ConceptIds() => (ConceptIdsCsv ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class SearchIgnoredTermRecord : BindableModel
{
    private string _term = string.Empty;
    private bool _isActive = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Term { get => _term; set => Set(ref _term, value); }
    public bool IsOfficial { get; set; }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public string OriginLabel => IsOfficial ? "Atlas" : "Client";
}

public sealed class SearchUnknownQueryRecord : BindableModel
{
    private int _count = 1;
    private DateTimeOffset _lastSeenUtc = DateTimeOffset.UtcNow;
    private int _lastResultCount;
    private string _lastInterpretation = string.Empty;
    private string _expectedInterpretation = string.Empty;
    private string _expectedConceptIdsCsv = string.Empty;
    private bool _isReviewed;

    public string Query { get; set; } = string.Empty;
    public string NormalizedQuery { get; set; } = string.Empty;
    public int Count { get => _count; set => Set(ref _count, Math.Max(1, value)); }
    public int LastResultCount
    {
        get => _lastResultCount;
        set { if (Set(ref _lastResultCount, Math.Max(0, value))) { Raise(nameof(ResultLabel)); Raise(nameof(ReviewLabel)); } }
    }
    public string LastInterpretation { get => _lastInterpretation; set => Set(ref _lastInterpretation, value); }
    public string ExpectedInterpretation { get => _expectedInterpretation; set => Set(ref _expectedInterpretation, value); }
    public string ExpectedConceptIdsCsv { get => _expectedConceptIdsCsv; set => Set(ref _expectedConceptIdsCsv, value); }
    public bool IsReviewed
    {
        get => _isReviewed;
        set { if (Set(ref _isReviewed, value)) Raise(nameof(ReviewLabel)); }
    }
    public DateTimeOffset LastSeenUtc
    {
        get => _lastSeenUtc;
        set { if (Set(ref _lastSeenUtc, value)) Raise(nameof(LastSeenLabel)); }
    }
    public string LastSeenLabel => LastSeenUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string ResultLabel => LastResultCount == 0 ? "Aucun" : LastResultCount.ToString();
    public string ReviewLabel => IsReviewed ? "Validée" : LastResultCount == 0 ? "À corriger" : "À vérifier";
}

public sealed class SearchDictionaryDocument
{
    public int SchemaVersion { get; set; } = 2;
    public int OfficialVersion { get; set; }
    public List<SearchConceptRecord> Concepts { get; set; } = [];
    public List<SearchSynonymEntry> Synonyms { get; set; } = [];
    public List<SearchExpressionRecord> Expressions { get; set; } = [];
    public List<SearchIgnoredTermRecord> IgnoredTerms { get; set; } = [];
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
    public List<string> RecognizedConceptIds { get; init; } = [];
    public List<string> IgnoredTerms { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}
