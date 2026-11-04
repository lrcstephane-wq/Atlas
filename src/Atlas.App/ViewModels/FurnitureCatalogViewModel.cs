using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Atlas.App.Infrastructure;
using Atlas.Core.Models;

namespace Atlas.App.ViewModels;

public sealed class FurnitureCatalogViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ListCollectionView _view;
    private string _search = string.Empty;
    private string _selectedStatus = "Tous les statuts";
    private string _selectedType = "Tous les types";
    private string _selectedSort = "Modification récente";
    private bool _incompleteOnly;
    private bool _isCardView;
    private FurnitureRecord? _selectedFurniture;
    private BitmapImage? _preview;

    public FurnitureCatalogViewModel(MainViewModel main)
    {
        _main = main;
        _view = new ListCollectionView((IList)main.Furniture) { Filter = FilterFurniture };
        StatusOptions = ["Tous les statuts", "Brouillon", "À contrôler", "Validée", "Retenue", "Publiée", "Archivée"];
        SortOptions = ["Modification récente", "Nom A–Z", "Référence", "Statut"];
        RebuildTypeOptions();
        ApplySort();
        main.Furniture.CollectionChanged += FurnitureOnCollectionChanged;
        main.FurnitureTypes.CollectionChanged += FurnitureTypesOnCollectionChanged;
        main.PropertyChanged += MainOnPropertyChanged;
        SelectedFurniture = main.SelectedFurniture is { } opened && _view.Contains(opened)
            ? opened
            : _view.Cast<FurnitureRecord>().FirstOrDefault();
    }

    public ICollectionView FurnitureView => _view;
    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> SortOptions { get; }
    public ObservableCollection<string> TypeOptions { get; } = [];

    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value ?? string.Empty)) Refresh(); }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set { if (SetProperty(ref _selectedStatus, value ?? "Tous les statuts")) Refresh(); }
    }

    public string SelectedType
    {
        get => _selectedType;
        set { if (SetProperty(ref _selectedType, value ?? "Tous les types")) Refresh(); }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (!SetProperty(ref _selectedSort, value ?? "Modification récente")) return;
            ApplySort();
            Refresh();
        }
    }

    public bool IncompleteOnly
    {
        get => _incompleteOnly;
        set { if (SetProperty(ref _incompleteOnly, value)) Refresh(); }
    }

    public bool IsCardView
    {
        get => _isCardView;
        set => SetProperty(ref _isCardView, value);
    }

    public FurnitureRecord? SelectedFurniture
    {
        get => _selectedFurniture;
        set
        {
            if (!SetProperty(ref _selectedFurniture, value)) return;
            LoadPreview();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedUniverses));
            OnPropertyChanged(nameof(SelectedUsages));
            OnPropertyChanged(nameof(SelectedCompositionLabel));
            OnPropertyChanged(nameof(IsSelectedFurnitureOpen));
        }
    }

    public BitmapImage? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public bool HasSelection => SelectedFurniture is not null;
    public bool IsSelectedFurnitureOpen => SelectedFurniture is not null && ReferenceEquals(SelectedFurniture, _main.SelectedFurniture);
    public string ResultLabel => ResultCount == 1 ? "1 fiche trouvée" : $"{ResultCount:N0} fiches trouvées";
    public int ResultCount => _view.Cast<object>().Count();
    public string SelectedUniverses => JoinValues(SelectedFurniture?.Universes, "Aucun univers");
    public string SelectedUsages => JoinValues(SelectedFurniture?.Usages, "Aucun usage spécifique");
    public string SelectedCompositionLabel
    {
        get
        {
            var count = SelectedFurniture?.ComponentLines?.Sum(line => line.Quantity) ?? 0;
            return count == 0 ? "Composition non renseignée" : count == 1 ? "1 composant" : $"{count} composants";
        }
    }

    public void ClearFilters()
    {
        Search = string.Empty;
        SelectedStatus = "Tous les statuts";
        SelectedType = "Tous les types";
        IncompleteOnly = false;
    }

    public void Refresh()
    {
        _view.Refresh();
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(ResultLabel));
        if (SelectedFurniture is not null && !_view.Cast<FurnitureRecord>().Contains(SelectedFurniture))
            SelectedFurniture = _view.Cast<FurnitureRecord>().FirstOrDefault();
    }

    public void Dispose()
    {
        _main.Furniture.CollectionChanged -= FurnitureOnCollectionChanged;
        _main.FurnitureTypes.CollectionChanged -= FurnitureTypesOnCollectionChanged;
        _main.PropertyChanged -= MainOnPropertyChanged;
    }

    private bool FilterFurniture(object item)
    {
        if (item is not FurnitureRecord furniture) return false;
        var expectedStatus = SelectedStatus switch
        {
            "Brouillon" => RecordStatus.Brouillon,
            "À contrôler" => RecordStatus.AControler,
            "Validée" => RecordStatus.Validee,
            "Retenue" => RecordStatus.Retenue,
            "Publiée" => RecordStatus.Publiee,
            "Archivée" => RecordStatus.Archivee,
            _ => (RecordStatus?)null
        };
        if (expectedStatus is null && furniture.Status == RecordStatus.Archivee) return false;
        if (expectedStatus is not null && furniture.Status != expectedStatus) return false;
        if (SelectedType != "Tous les types" && !furniture.TypeMeuble.Equals(SelectedType, StringComparison.OrdinalIgnoreCase)) return false;
        if (IncompleteOnly && !IsIncomplete(furniture)) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var query = Search.Trim();
        return new[]
        {
            furniture.Reference, furniture.DisplayName, furniture.Family, furniture.Description, furniture.TypeMeuble,
            furniture.Forme, furniture.UsageSpecifique, string.Join(" ", furniture.Universes), string.Join(" ", furniture.Usages)
        }.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsIncomplete(FurnitureRecord furniture) =>
        string.IsNullOrWhiteSpace(furniture.DisplayName)
        || string.IsNullOrWhiteSpace(furniture.Reference)
        || string.IsNullOrWhiteSpace(furniture.TypeMeuble)
        || furniture.Universes.Count == 0
        || furniture.ComponentLines.Count == 0
        || string.IsNullOrWhiteSpace(furniture.SourceRelativePath);

    private void ApplySort() => _view.CustomSort = new FurnitureComparer(() => SelectedSort);

    private void LoadPreview()
    {
        Preview = null;
        var imagePath = _main.ResolveFurnitureImageAssetPath(SelectedFurniture);
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 720;
            image.UriSource = new Uri(imagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            Preview = image;
        }
        catch { Preview = null; }
    }

    private void RebuildTypeOptions()
    {
        var selected = SelectedType;
        TypeOptions.Clear();
        TypeOptions.Add("Tous les types");
        foreach (var type in _main.FurnitureTypes.Where(value => !string.IsNullOrWhiteSpace(value)).Order(StringComparer.CurrentCultureIgnoreCase))
            TypeOptions.Add(type);
        if (!TypeOptions.Contains(selected)) SelectedType = "Tous les types";
    }

    private void FurnitureOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void FurnitureTypesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTypeOptions();
    private void MainOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedFurniture)) return;
        if (_main.SelectedFurniture is { } opened && _view.Contains(opened)) SelectedFurniture = opened;
        OnPropertyChanged(nameof(IsSelectedFurnitureOpen));
    }

    private static string JoinValues(IEnumerable<string>? values, string fallback)
    {
        var result = string.Join(" · ", values?.Where(value => !string.IsNullOrWhiteSpace(value)) ?? []);
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private sealed class FurnitureComparer(Func<string> selectedSort) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not FurnitureRecord left || y is not FurnitureRecord right) return 0;
            var comparison = selectedSort() switch
            {
                "Nom A–Z" => CompareText(left.DisplayName, right.DisplayName),
                "Référence" => CompareText(left.Reference, right.Reference),
                "Statut" => left.Status != right.Status ? left.Status.CompareTo(right.Status) : CompareText(left.DisplayName, right.DisplayName),
                _ => Comparer<DateTimeOffset>.Default.Compare(right.ModifiedUtc ?? right.CreatedUtc, left.ModifiedUtc ?? left.CreatedUtc)
            };
            return comparison != 0 ? comparison : CompareText(left.Reference, right.Reference);
        }

        private static int CompareText(string? left, string? right) => StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
    }
}
