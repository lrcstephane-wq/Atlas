using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Atlas.Core.Models;

namespace Atlas.App.ViewModels;

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
            if (args.PropertyName == nameof(ComponentRecord.DisplayName)) OnPropertyChanged(nameof(Name));
            if (args.PropertyName == nameof(ComponentRecord.Status)) OnPropertyChanged(nameof(Status));
        };
    }

    public ComponentRecord Record { get; }
    public string Id => Record.Id;
    public string Name => Record.DisplayName;
    public string TechnicalName => Record.TechnicalName;
    public string Library => Record.LibraryName;
    public string Family => Record.FamilyName;
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

public sealed class FurnitureCardViewModel
{
    private readonly string _libraryRoot;
    private bool _thumbnailLoaded;
    private BitmapImage? _thumbnail;

    public FurnitureCardViewModel(FurnitureRecord record, string libraryRoot)
    {
        Record = record;
        _libraryRoot = libraryRoot;
    }

    public FurnitureRecord Record { get; }
    public string DisplayName => Record.DisplayName;
    public string Reference => Record.Reference;
    public string TypeMeuble => Record.TypeMeuble;
    public string Universes => string.Join(" · ", Record.Universes);
    public string Description => Record.Description;
    public string Forme => Record.Forme;

    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            if (string.IsNullOrWhiteSpace(Record.ImageRelativePath)) return null;
            var path = Path.Combine(_libraryRoot, Record.ImageRelativePath);
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
}

public sealed class LibraryFilterViewModel : INotifyPropertyChanged
{
    private int _markedCount;
    public required string Name { get; init; }
    public int TotalCount { get; init; }
    public int MarkedCount { get => _markedCount; set { if (_markedCount == value) return; _markedCount = value; PropertyChanged?.Invoke(this, new(nameof(MarkedCount))); PropertyChanged?.Invoke(this, new(nameof(CountLabel))); } }
    public string CountLabel => MarkedCount == 0 ? TotalCount.ToString("N0") : $"{MarkedCount}/{TotalCount}";
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
