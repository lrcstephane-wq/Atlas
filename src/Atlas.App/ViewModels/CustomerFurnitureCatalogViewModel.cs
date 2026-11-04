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

public sealed class CustomerFurnitureCatalogViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ListCollectionView _view;
    private string _search = string.Empty;
    private string _selectedLocation = "Tous les emplacements";
    private string _selectedSort = "Modification récente";
    private bool _isCardView = true;
    private CustomerFurnitureItem? _selectedFurniture;
    private BitmapImage? _preview;

    public CustomerFurnitureCatalogViewModel(MainViewModel main)
    {
        _main = main;
        _view = new ListCollectionView((IList)main.CustomerFurniture) { Filter = FilterFurniture };
        SortOptions = ["Modification récente", "Nom A–Z", "Fichier A–Z"];
        RebuildLocationOptions();
        ApplySort();
        main.CustomerFurniture.CollectionChanged += FurnitureOnCollectionChanged;
        main.CustomerFurnitureLocations.CollectionChanged += LocationsOnCollectionChanged;
        SelectedFurniture = main.SelectedCustomerFurniture is { } opened && _view.Contains(opened)
            ? opened
            : _view.Cast<CustomerFurnitureItem>().FirstOrDefault();
    }

    public ICollectionView FurnitureView => _view;
    public IReadOnlyList<string> SortOptions { get; }
    public ObservableCollection<string> LocationOptions { get; } = [];
    public bool CanEdit => _main.CanManageCustomerFurniture;
    public string AccessLabel => CanEdit ? "Double-cliquez pour modifier le meuble." : "Consultation seule — licence Créateur requise pour modifier.";

    public string Search { get => _search; set { if (SetProperty(ref _search, value ?? string.Empty)) Refresh(); } }
    public string SelectedLocation { get => _selectedLocation; set { if (SetProperty(ref _selectedLocation, value ?? "Tous les emplacements")) Refresh(); } }
    public string SelectedSort { get => _selectedSort; set { if (SetProperty(ref _selectedSort, value ?? "Modification récente")) { ApplySort(); Refresh(); } } }
    public bool IsCardView { get => _isCardView; set => SetProperty(ref _isCardView, value); }
    public CustomerFurnitureItem? SelectedFurniture
    {
        get => _selectedFurniture;
        set
        {
            if (!SetProperty(ref _selectedFurniture, value)) return;
            LoadPreview();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedLocationLabel));
            OnPropertyChanged(nameof(SelectedSourcePath));
        }
    }

    public BitmapImage? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public bool HasSelection => SelectedFurniture is not null;
    public string ResultLabel => ResultCount == 1 ? "1 meuble trouvé" : $"{ResultCount:N0} meubles trouvés";
    public int ResultCount => _view.Cast<object>().Count();
    public string SelectedLocationLabel => _main.CustomerFurnitureLocationLabel(SelectedFurniture);
    public string SelectedSourcePath => _main.ResolveCustomerFurnitureSourcePath(SelectedFurniture);

    public string LocationLabel(CustomerFurnitureItem? item) => _main.CustomerFurnitureLocationLabel(item);
    public string ImagePath(CustomerFurnitureItem? item) => _main.ResolveCustomerFurnitureImagePath(item);

    public void ClearFilters() { Search = string.Empty; SelectedLocation = "Tous les emplacements"; }
    public void Refresh()
    {
        _view.Refresh();
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(ResultLabel));
        if (SelectedFurniture is not null && !_view.Cast<CustomerFurnitureItem>().Contains(SelectedFurniture))
            SelectedFurniture = _view.Cast<CustomerFurnitureItem>().FirstOrDefault();
    }

    public void Dispose()
    {
        _main.CustomerFurniture.CollectionChanged -= FurnitureOnCollectionChanged;
        _main.CustomerFurnitureLocations.CollectionChanged -= LocationsOnCollectionChanged;
    }

    private bool FilterFurniture(object item)
    {
        if (item is not CustomerFurnitureItem furniture) return false;
        if (SelectedLocation != "Tous les emplacements" && !_main.CustomerFurnitureLocationLabel(furniture).Equals(SelectedLocation, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var query = Search.Trim();
        return new[] { furniture.DisplayName, furniture.Description, furniture.RelativeTopPath, _main.CustomerFurnitureLocationLabel(furniture) }
            .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void ApplySort() => _view.CustomSort = new CustomerFurnitureComparer(() => SelectedSort);

    private void RebuildLocationOptions()
    {
        var selected = SelectedLocation;
        LocationOptions.Clear();
        LocationOptions.Add("Tous les emplacements");
        foreach (var name in _main.CustomerFurnitureLocations.Select(location => location.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
            LocationOptions.Add(name);
        if (!LocationOptions.Contains(selected)) SelectedLocation = "Tous les emplacements";
    }

    private void LoadPreview()
    {
        Preview = null;
        var path = _main.ResolveCustomerFurnitureImagePath(SelectedFurniture);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 720;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            Preview = image;
        }
        catch { Preview = null; }
    }

    private void FurnitureOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void LocationsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { RebuildLocationOptions(); Refresh(); }

    private sealed class CustomerFurnitureComparer(Func<string> selectedSort) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not CustomerFurnitureItem left || y is not CustomerFurnitureItem right) return 0;
            var comparison = selectedSort() switch
            {
                "Nom A–Z" => CompareText(left.DisplayName, right.DisplayName),
                "Fichier A–Z" => CompareText(left.RelativeTopPath, right.RelativeTopPath),
                _ => Comparer<DateTimeOffset>.Default.Compare(right.LastSeenUtc, left.LastSeenUtc)
            };
            return comparison != 0 ? comparison : CompareText(left.RelativeTopPath, right.RelativeTopPath);
        }
        private static int CompareText(string? left, string? right) => StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
    }
}
