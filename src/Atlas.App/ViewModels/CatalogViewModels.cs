using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Atlas.Core.Models;
using Atlas.Core.Services;

namespace Atlas.App.ViewModels;

public sealed class FurnitureStepViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    public FurnitureStepViewModel(string key, string number, string label) { Key = key; Number = number; Label = label; }
    public string Key { get; }
    public string Number { get; }
    public string Label { get; }
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new(nameof(IsActive)));
            PropertyChanged?.Invoke(this, new(nameof(Background)));
            PropertyChanged?.Invoke(this, new(nameof(BorderBrush)));
            PropertyChanged?.Invoke(this, new(nameof(Foreground)));
        }
    }
    public string Background => IsActive ? "#397FF6" : "#1C2B42";
    public string BorderBrush => IsActive ? "#2DD4BF" : "#283A55";
    public string Foreground => IsActive ? "#FFFFFF" : "#B9C7DA";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ComponentCardViewModel : INotifyPropertyChanged
{
    private bool _isMarked;
    private bool _thumbnailLoaded;
    private BitmapImage? _thumbnail;
    private readonly string _libraryRoot;

    public ComponentCardViewModel(ComponentRecord record, string libraryRoot)
    {
        Record = record;
        _libraryRoot = libraryRoot;
        Record.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ComponentRecord.DisplayName)) { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(DetailedName)); }
            if (args.PropertyName == nameof(ComponentRecord.TechnicalName)) { OnPropertyChanged(nameof(TechnicalName)); OnPropertyChanged(nameof(DetailedName)); }
            if (args.PropertyName is nameof(ComponentRecord.FamilyName) or nameof(ComponentRecord.AtlasFamilyNameOverride))
            {
                OnPropertyChanged(nameof(Family));
                OnPropertyChanged(nameof(Location));
            }
            if (args.PropertyName == nameof(ComponentRecord.Status)) OnPropertyChanged(nameof(Status));
        };
    }

    public ComponentRecord Record { get; }
    public string Id => Record.Id;
    public string Name => Record.DisplayName;
    public string DetailedName => ComponentNameParser.SuggestDetailedDisplayName(Name, Record.TechnicalName);
    public string TechnicalName => Record.TechnicalName;
    public string Library => Record.LibraryName;
    public string Family => Record.EffectiveFamilyName;
    public string Type => string.IsNullOrWhiteSpace(Record.TypeCode) ? "Non classé" : Record.TypeCode;
    public string Location => $"{Library}  ›  {Family}";
    public string Status => Record.IsMissing ? "Fichier absent" : Record.IsNameCompliant ? Record.Status.ToString() : "À contrôler";
    public bool HasWarning => Record.IsMissing || !Record.IsNameCompliant;
    public bool IsMarked { get => _isMarked; set { if (_isMarked == value) return; _isMarked = value; OnPropertyChanged(); } }

    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            if (string.IsNullOrWhiteSpace(Record.PreviewRelativePath)) return null;
            var path = Path.Combine(_libraryRoot, Record.PreviewRelativePath);
            if (!File.Exists(path)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 260;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                _thumbnail = image;
            }
            catch { _thumbnail = null; }
            return _thumbnail;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FurnitureCardViewModel : INotifyPropertyChanged
{
    private readonly string _furnitureRoot;
    private readonly string _fallbackRoot;
    private bool _thumbnailLoaded;
    private bool _isChosen;
    private double _searchScore;
    private BitmapImage? _thumbnail;

    public FurnitureCardViewModel(FurnitureRecord record, string furnitureRoot, string? fallbackRoot = null)
    {
        Record = record;
        _furnitureRoot = furnitureRoot;
        _fallbackRoot = fallbackRoot ?? furnitureRoot;
    }

    public FurnitureRecord Record { get; }
    public string DisplayName => Record.DisplayName;
    public string Reference => Record.Reference;
    public string TypeMeuble => Record.TypeMeuble;
    public string Universes => string.Join(" · ", Record.Universes);
    public string Description => Record.Description;
    public string Forme => Record.Forme;
    public bool IsChosen
    {
        get => _isChosen;
        set
        {
            if (_isChosen == value) return;
            _isChosen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChosen)));
        }
    }
    public double SearchScore
    {
        get => _searchScore;
        set
        {
            if (Math.Abs(_searchScore - value) < 0.0001d) return;
            _searchScore = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchScore)));
        }
    }

    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            if (string.IsNullOrWhiteSpace(Record.ImageRelativePath)) return null;
            var path = Path.IsPathRooted(Record.ImageRelativePath) ? Record.ImageRelativePath : Path.Combine(_furnitureRoot, Record.ImageRelativePath);
            if (!File.Exists(path) && !_fallbackRoot.Equals(_furnitureRoot, StringComparison.OrdinalIgnoreCase)) path = Path.Combine(_fallbackRoot, Record.ImageRelativePath);
            if (!File.Exists(path)) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 420;
                image.UriSource = new Uri(path, UriKind.Absolute); image.EndInit(); image.Freeze(); _thumbnail = image;
            }
            catch { _thumbnail = null; }
            return _thumbnail;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LibraryFilterViewModel : INotifyPropertyChanged
{
    private int _markedCount;
    public required string Name { get; init; }
    public int TotalCount { get; init; }
    public int MarkedCount { get => _markedCount; set { if (_markedCount == value) return; _markedCount = value; PropertyChanged?.Invoke(this, new(nameof(MarkedCount))); PropertyChanged?.Invoke(this, new(nameof(CountLabel))); } }
    public string CountLabel => MarkedCount == 0 ? TotalCount.ToString("N0") : $"{MarkedCount}/{TotalCount}";
    public override string ToString() => Name;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FurnitureCompositionLineViewModel : INotifyPropertyChanged
{
    private bool _isMarked;
    private readonly ComponentCardViewModel _card;

    public FurnitureCompositionLineViewModel(FurnitureComponentLine line, ComponentCardViewModel card)
    {
        Line = line;
        _card = card;
        Line.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FurnitureComponentLine.Quantity)) PropertyChanged?.Invoke(this, new(nameof(Quantity)));
        };
    }

    public FurnitureComponentLine Line { get; }
    public string ComponentId => Line.ComponentId;
    public string Name => _card.DetailedName;
    public string Family => _card.Family;
    public string Location => _card.Location;
    public BitmapImage? Thumbnail => _card.Thumbnail;
    public int Quantity { get => Line.Quantity; set => Line.Quantity = value; }
    public bool IsMarked { get => _isMarked; set { if (_isMarked == value) return; _isMarked = value; PropertyChanged?.Invoke(this, new(nameof(IsMarked))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FilterOptionViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    public required string Label { get; init; }
    public int Count { get; init; }
    public bool IsActive { get => _isActive; set { if (_isActive == value) return; _isActive = value; PropertyChanged?.Invoke(this, new(nameof(IsActive))); PropertyChanged?.Invoke(this, new(nameof(Background))); } }
    public string Background => IsActive ? "#2257B6" : "#1C2B42";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CatalogFacetViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFavorite;
    private readonly Action<CatalogFacetViewModel> _changed;

    public CatalogFacetViewModel(string group, string label, int count, Action<CatalogFacetViewModel> changed, string description = "", BitmapImage? image = null)
    {
        Group = group;
        Label = label;
        Count = count;
        _changed = changed;
        Description = description;
        Image = image;
    }

    public string Group { get; }
    public string Label { get; }
    public int Count { get; }
    public string Description { get; }
    public BitmapImage? Image { get; }
    public bool HasImage => Image is not null;
    public string CountLabel => Count.ToString("N0");
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            _changed(this);
        }
    }
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new(nameof(FavoriteGlyph)));
        }
    }
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteKey => $"{Group}|{CatalogSearchEngine.Normalize(Label)}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ToggleOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private readonly Action<ToggleOptionViewModel>? _changed;

    public ToggleOptionViewModel(string label, bool isSelected, Action<ToggleOptionViewModel>? changed = null)
    {
        Label = label;
        _isSelected = isSelected;
        _changed = changed;
    }

    public string Label { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _changed?.Invoke(this);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TagChoiceViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _exclusionReason;
    private readonly Action<TagChoiceViewModel> _changed;
    private readonly Action<TagChoiceViewModel>? _reasonChanged;

    public TagChoiceViewModel(ComponentTagRecord tag, string originLabel, bool inherited, bool selected, string exclusionReason, Action<TagChoiceViewModel> changed, Action<TagChoiceViewModel>? reasonChanged = null)
    {
        Tag = tag;
        OriginLabel = originLabel;
        IsInherited = inherited;
        _isSelected = selected;
        _exclusionReason = exclusionReason;
        _changed = changed;
        _reasonChanged = reasonChanged;
    }

    public ComponentTagRecord Tag { get; }
    public string Id => Tag.Id;
    public string Label => Tag.Label;
    public bool IsInherited { get; }
    public string OriginLabel { get; }
    public bool IsExcluded => IsInherited && !IsSelected;
    public string ExclusionReason
    {
        get => _exclusionReason;
        set
        {
            if (_exclusionReason == value) return;
            _exclusionReason = value;
            PropertyChanged?.Invoke(this, new(nameof(ExclusionReason)));
            _reasonChanged?.Invoke(this);
        }
    }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new(nameof(IsExcluded)));
            _changed(this);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
