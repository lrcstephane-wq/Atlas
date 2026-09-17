using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Atlas.App.Infrastructure;
using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Models;
using Atlas.Core.Services;
using Microsoft.Win32;

namespace Atlas.App;

public sealed class MainViewModel : ObservableObject
{
    public event EventHandler? TaxonomyChanged;

    private static readonly string[] DefaultUniverses = ["Cuisine", "Dressing", "Salle de bain", "Bibliothèque", "Séjour", "Bureau / Tertiaire", "Buanderie", "Agencement commercial", "Chambre", "Hôtellerie / Hébergement", "Restaurant / Bar"];
    private readonly SharedCatalogStore _store;
    private readonly UserAccountStore _userStore;
    private readonly LocalBootstrap _bootstrap;
    private readonly LibraryScanner _scanner = new();
    private readonly ApplicationUpdateService _updater = new();
    private readonly HorizonPreferencesStore _horizonPreferencesStore;
    private HorizonPreferences _horizonPreferences = new();
    private AtlasCatalog _catalog = new();
    private ComponentTaxonomy _taxonomy = new();
    private string _currentPage = "Dashboard", _componentSearch = "", _furnitureSearch = "", _clientSearch = "", _statusText = "Initialisation…", _updateLabel = "Rechercher une mise à jour";
    private string _sharedRoot, _activeFamilyFilter = "Toutes", _selectedComponentType = "Tous les types", _selectedClientUniverse = "Tous les univers", _selectedClientType = "Tous les types", _selectedClientFamily = "Toutes les familles", _selectedClientTag = "Tous les tags", _selectedFurnitureStatus = "Tous les statuts";
    private string _currentFurnitureStep = "Identity", _creationMode = "Quick", _newUniverseName = "";
    private ComponentCardViewModel? _selectedComponentCard;
    private FurnitureRecord? _selectedFurniture;
    private ComponentRecord? _selectedCompositionCandidate, _selectedLinkedComponent;
    private FurnitureCardViewModel? _selectedClientFurnitureCard;
    private FurnitureFamilyRecord? _selectedFurnitureFamily;
    private LibraryFilterViewModel? _selectedLibraryFilter;
    private bool _isNavigationExpanded = true, _isComponentMosaic = true, _isBusy, _showAdvancedClientFilters, _suppressClientFacetRefresh;
    private bool _isTopSolidBridgeReady;
    private string _topSolidBridgeFolder = string.Empty;
    private IReadOnlyList<string> _topSolidBridgeFiles = Array.Empty<string>();
    private BitmapImage? _furniturePreview;
    private readonly HashSet<FurnitureCompositionLineViewModel> _selectedCompositionLines = [];

    public MainViewModel(SharedCatalogStore store, UserAccountStore userStore, LocalBootstrap bootstrap, UserAccount currentUser)
    {
        _store = store; _userStore = userStore; _bootstrap = bootstrap; _sharedRoot = bootstrap.SharedRoot; CurrentUser = currentUser;
        _horizonPreferencesStore = new HorizonPreferencesStore(_sharedRoot, currentUser.Id);
        FurnitureSteps[0].IsActive = true;
        ComponentView = CollectionViewSource.GetDefaultView(ComponentCards); ComponentView.Filter = FilterComponent;
        FurnitureView = CollectionViewSource.GetDefaultView(Furniture); FurnitureView.Filter = FilterFurniture;
        ClientFurnitureView = CollectionViewSource.GetDefaultView(ClientFurnitureCards); ClientFurnitureView.Filter = FilterClientFurniture;
        if (ClientFurnitureView is ListCollectionView clientListView) clientListView.CustomSort = new ClientFurnitureSearchComparer(() => ClientSearch);

        NavigateCommand = new(page => CurrentPage = page?.ToString() ?? "Dashboard");
        ToggleNavigationCommand = new(_ => IsNavigationExpanded = !IsNavigationExpanded);
        SaveCommand = new(_ => _ = SaveAsync(), _ => CanEdit && !IsBusy);
        ReloadCommand = new(_ => _ = ReloadAsync(), _ => !IsBusy);
        ScanCommand = new(_ => _ = ScanAsync(), _ => CanEdit && !IsBusy);
        ChooseLibraryCommand = new(_ => ChooseLibrary(), _ => CanEdit);
        ChooseSharedRootCommand = new(_ => ChooseSharedRoot(), _ => IsAdministrator);
        SaveBootstrapCommand = new(_ => _ = SaveBootstrapAsync(), _ => IsAdministrator);
        ValidateComponentCommand = new(_ => ValidateComponent(), _ => CanValidate && SelectedComponent is not null);
        SetComponentLayoutCommand = new(value => IsComponentMosaic = value?.ToString() != "List");
        SetFamilyFilterCommand = new(value => ActiveFamilyFilter = value?.ToString() ?? "Toutes");
        ClearComponentFiltersCommand = new(_ => ClearComponentFilters());
        SelectVisibleComponentsCommand = new(_ => MarkVisibleComponents(true), _ => CanEdit);
        ClearMarkedComponentsCommand = new(_ => MarkVisibleComponents(false), _ => CanEdit);
        ClearClientFiltersCommand = new(_ => ClearClientFilters());
        ToggleClientFiltersCommand = new(_ => ShowAdvancedClientFilters = !ShowAdvancedClientFilters);
        ClearClientSelectionCommand = new(_ => ClearClientSelection(), _ => ClientSelectionCount > 0);
        PrepareTopSolidBridgeCommand = new(_ => PrepareTopSolidBridge(), _ => ClientSelectionCount > 0 && !IsBusy);
        OpenTopSolidBridgeFolderCommand = new(_ => OpenTopSolidBridgeFolder(), _ => IsTopSolidBridgeReady && Directory.Exists(TopSolidBridgeFolder));
        CreateFurnitureCommand = new(_ => CreateFurniture(), _ => CanEdit);
        CreateFamilyCommand = new(_ => CreateFamily(), _ => CanEdit);
        SetCreationModeCommand = new(value => CreationMode = value?.ToString() ?? "Quick");
        NavigateFurnitureStepCommand = new(value => CurrentFurnitureStep = value?.ToString() ?? "Identity");
        PreviousFurnitureStepCommand = new(_ => MoveFurnitureStep(-1), _ => FurnitureStepIndex > 0);
        NextFurnitureStepCommand = new(_ => MoveFurnitureStep(1), _ => FurnitureStepIndex < FurnitureSteps.Count - 1);
        ChooseFurnitureTopCommand = new(_ => ChooseFurnitureTop(), _ => CanEdit && SelectedFurniture is not null);
        OpenFurnitureFolderCommand = new(_ => OpenFurnitureFolder(), _ => SelectedFurniture is not null && !string.IsNullOrWhiteSpace(SelectedFurniture.SourceRelativePath));
        AddUniverseCommand = new(_ => AddUniverse(), _ => CanEdit && !string.IsNullOrWhiteSpace(NewUniverseName));
        AddComponentCommand = new(_ => AddComponent(), _ => CanEdit && SelectedFurniture is not null && SelectedCompositionCandidate is not null);
        AddMarkedComponentsCommand = new(_ => AddMarkedComponents(), _ => CanEdit && SelectedFurniture is not null && MarkedComponentCount > 0);
        RemoveComponentCommand = new(_ => RemoveComponent(), _ => CanEdit && SelectedFurniture is not null && SelectedLinkedComponent is not null);
        RemoveMarkedCompositionCommand = new(_ => RemoveSelectedComposition(), _ => CanEdit && SelectedFurniture is not null && _selectedCompositionLines.Count > 0);
        PublishFurnitureCommand = new(_ => _ = PublishFurnitureAsync(), _ => CanValidate && SelectedFurniture is not null && !IsBusy);
        OpenSharedRootCommand = new(_ => OpenSharedRoot());
        CheckUpdateCommand = new(_ => _ = CheckUpdateAsync(false), _ => !IsBusy);
    }

    public UserAccount CurrentUser { get; }
    public bool CanEdit => CurrentUser.CanEdit;
    public bool CanValidate => CurrentUser.CanValidate;
    public bool IsAdministrator => CurrentUser.IsAdministrator;
    public ObservableCollection<ComponentRecord> Components { get; } = [];
    public ObservableCollection<ComponentCardViewModel> ComponentCards { get; } = [];
    public ObservableCollection<FurnitureRecord> Furniture { get; } = [];
    public ObservableCollection<FurnitureFamilyRecord> FurnitureFamilies { get; } = [];
    public ObservableCollection<FurnitureCardViewModel> ClientFurnitureCards { get; } = [];
    public ObservableCollection<ComponentRecord> LinkedComponents { get; } = [];
    public ObservableCollection<FurnitureCompositionLineViewModel> CompositionLines { get; } = [];
    public ObservableCollection<UserAccount> Users { get; } = [];
    public ObservableCollection<LibraryFilterViewModel> LibraryFilters { get; } = [];
    public ObservableCollection<FilterOptionViewModel> FamilyFilters { get; } = [];
    public ObservableCollection<ToggleOptionViewModel> UniverseOptions { get; } = [];
    public ObservableCollection<ToggleOptionViewModel> UsageOptions { get; } = [];
    public ObservableCollection<ToggleOptionViewModel> FurnitureTagOptions { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientUniverseFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientTypeFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientUsageFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientFamilyFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientTagFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientFormFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientConstructionFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientAssemblyFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientBackFacets { get; } = [];
    public ObservableCollection<CatalogFacetViewModel> ClientFavoriteFacets { get; } = [];
    public ObservableCollection<FurnitureStepViewModel> FurnitureSteps { get; } =
    [
        new("Identity", "01", "Identité"),
        new("Composition", "02", "Composition"),
        new("Classification", "03", "Classement"),
        new("Structure", "04", "Structure"),
        new("Review", "05", "Contrôle")
    ];
    public ObservableCollection<string> ClientUniverseOptions { get; } = ["Tous les univers"];
    public ObservableCollection<string> ComponentTypeOptions { get; } = ["Tous les types"];
    public ObservableCollection<string> ClientTypeOptions { get; } = ["Tous les types"];
    public ObservableCollection<string> ClientFamilyOptions { get; } = ["Toutes les familles"];
    public ObservableCollection<string> ClientTagOptions { get; } = ["Tous les tags"];
    public IReadOnlyList<string> FurnitureStatusOptions { get; } = ["Tous les statuts", "Brouillon", "À contrôler", "Validée", "Retenue", "Publiée"];
    public ICollectionView ComponentView { get; }
    public ICollectionView FurnitureView { get; }
    public ICollectionView ClientFurnitureView { get; }
    public IReadOnlyList<CatalogEnvironment> Environments { get; } = Enum.GetValues<CatalogEnvironment>();
    public IReadOnlyList<RecordStatus> Statuses { get; } = Enum.GetValues<RecordStatus>();
    public IReadOnlyList<string> RoleProfiles { get; } = ["Lecture seule", "Éditeur", "Éditeur + validateur", "Administrateur"];
    public IReadOnlyList<string> FurnitureTypes { get; } = ["Meuble bas", "Meuble haut", "Colonne", "Demi-colonne", "Niche", "Armoire", "Étagère", "Banc", "Bureau", "Console", "Comptoir", "Présentoir"];
    public IReadOnlyList<string> FurnitureUsages { get; } = ["Sous-évier", "Vasque", "Four", "Micro-ondes", "Réfrigérateur", "Lave-vaisselle", "Lave-linge", "Sèche-linge", "Poubelle", "Penderie", "Chaussures", "TV / multimédia", "Imprimante", "Caisse", "Présentation / exposition", "Technique"];
    public IReadOnlyList<string> FurnitureForms { get; } = ["Droit", "Angle", "Courbe", "Trapèze", "Pan coupé", "Sous rampant"];
    public IReadOnlyList<string> ConstructionPrinciples { get; } = ["Non applicable", "Montant filant", "Traverse filante"];
    public IReadOnlyList<string> BackPositions { get; } = ["Non applicable", "Sans dos", "Appliqué", "Rainuré", "Intérieur"];
    public IReadOnlyList<string> AssemblyTypes { get; } = ["Sans assemblage", "Vis", "Tourillons", "Tourillons + vis", "Excentrique", "Tourillons + excentriques", "Clamex", "Cabineo", "Vis auto-tourillonnante"];
    public IReadOnlyList<string> TagCategories { get; } = ["Fonction", "Marque", "Gamme", "Implantation", "Technologie", "Système", "Autre"];
    public IReadOnlyList<string> DoorTypes { get; } = ["Applique", "Semi-applique", "Encastrée"];
    public IReadOnlyList<string> DrawerTypes { get; } = ["Applique", "Encastré"];
    public IReadOnlyList<string> CatalogUniverses => _catalog.UniverseDefinitions
        .Where(item => item.IsActive)
        .OrderBy(item => item.SortOrder)
        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(item => item.Name)
        .ToList();
    public IReadOnlyList<CatalogUniverseRecord> CatalogUniverseDefinitions => _catalog.UniverseDefinitions;

    public WorkspaceSettings Settings => _catalog.Settings;
    public string Version => _updater.CurrentVersion;
    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            foreach (var property in new[] { nameof(DashboardNavBackground), nameof(ComponentsNavBackground), nameof(FurnitureNavBackground), nameof(FutureNavBackground), nameof(CatalogNavBackground), nameof(SettingsNavBackground) }) OnPropertyChanged(property);
            OnPropertyChanged(nameof(IsCatalogPage));
        }
    }
    public bool IsCatalogPage => CurrentPage.Equals("Catalog", StringComparison.OrdinalIgnoreCase);
    public string DashboardNavBackground => NavBackground("Dashboard");
    public string ComponentsNavBackground => NavBackground("Components");
    public string FurnitureNavBackground => NavBackground("Furniture");
    public string FutureNavBackground => NavBackground("Future");
    public string CatalogNavBackground => NavBackground("Catalog");
    public string SettingsNavBackground => NavBackground("Settings");
    public string SharedRoot { get => _sharedRoot; set => SetProperty(ref _sharedRoot, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string UpdateLabel { get => _updateLabel; set => SetProperty(ref _updateLabel, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); } }
    public bool IsNavigationExpanded { get => _isNavigationExpanded; set { if (SetProperty(ref _isNavigationExpanded, value)) OnPropertyChanged(nameof(NavigationWidth)); } }
    public double NavigationWidth => IsNavigationExpanded ? 224 : 76;
    public bool IsComponentMosaic { get => _isComponentMosaic; set => SetProperty(ref _isComponentMosaic, value); }
    public bool ShowAdvancedClientFilters { get => _showAdvancedClientFilters; set => SetProperty(ref _showAdvancedClientFilters, value); }
    public bool IsFamilyMode => CreationMode == "Family";
    public string EnvironmentLabel => Settings.Environment == CatalogEnvironment.NonConfigure ? "ENV. À CONFIGURER" : $"ENV. {Settings.Environment}";
    public int ComponentCount => Components.Count(item => !item.IsDemo);
    public int FurnitureCount => Furniture.Count(item => !item.IsDemo);
    public int PublishedCount => Furniture.Count(item => item.Status == RecordStatus.Publiee);
    public int ClientResultCount => ClientFurnitureView.Cast<object>().Count();
    public bool HasClientResults => ClientResultCount > 0;
    public int ActiveClientFilterCount => ClientFacetGroups().Sum(group => group.Count(option => option.IsSelected));
    public int ClientSelectionCount => ClientFurnitureCards.Count(item => item.IsChosen);
    public string ClientSelectionLabel => ClientSelectionCount == 0 ? "Aucun meuble sélectionné" : ClientSelectionCount == 1 ? "1 meuble sélectionné" : $"{ClientSelectionCount} meubles sélectionnés";
    public bool IsTopSolidBridgeReady { get => _isTopSolidBridgeReady; private set => SetProperty(ref _isTopSolidBridgeReady, value); }
    public string TopSolidBridgeFolder { get => _topSolidBridgeFolder; private set => SetProperty(ref _topSolidBridgeFolder, value); }
    public IReadOnlyList<string> TopSolidBridgeFiles { get => _topSolidBridgeFiles; private set => SetProperty(ref _topSolidBridgeFiles, value); }
    public string TopSolidBridgeCountLabel => TopSolidBridgeFiles.Count == 1 ? "1 copie prête" : $"{TopSolidBridgeFiles.Count} copies prêtes";
    public string ClientSearchInterpretation { get; private set; } = string.Empty;
    public bool HasClientSearchInterpretation => !string.IsNullOrWhiteSpace(ClientSearchInterpretation);
    public bool HasClientFavorites => ClientFavoriteFacets.Count > 0;
    public string ClientResultLabel => ClientResultCount <= 1 ? $"{ClientResultCount} meuble trouvé" : $"{ClientResultCount} meubles trouvés";
    public string ClientFilterButtonLabel => ActiveClientFilterCount == 0 ? "Filtres techniques" : $"Filtres techniques · {ActiveClientFilterCount}";
    public int HealthIssueCount => Components.Count(item => !item.IsNameCompliant || item.IsMissing) + Furniture.Count(item => item.ComponentIds.Count == 0);
    public int MarkedComponentCount => ComponentCards.Count(item => item.IsMarked);
    public string VisibleComponentLabel => $"{ComponentView.Cast<object>().Count():N0} affichés sur {ScopedComponentCards().Count():N0}";
    public string LastModification => _catalog.Revision == 0 ? "Espace de découverte" : $"Révision {_catalog.Revision} · {_catalog.ModifiedBy}";

    public string ComponentSearch { get => _componentSearch; set { if (SetProperty(ref _componentSearch, value)) RefreshComponentView(); } }
    public string FurnitureSearch { get => _furnitureSearch; set { if (SetProperty(ref _furnitureSearch, value)) RefreshFurnitureView(); } }
    public string SelectedFurnitureStatus { get => _selectedFurnitureStatus; set { if (SetProperty(ref _selectedFurnitureStatus, value ?? "Tous les statuts")) RefreshFurnitureView(); } }
    public string ClientSearch { get => _clientSearch; set { if (SetProperty(ref _clientSearch, value)) RefreshClientFurnitureView(); } }
    public string ActiveFamilyFilter { get => _activeFamilyFilter; set { if (SetProperty(ref _activeFamilyFilter, value)) { UpdateFamilyFilterStates(); RebuildComponentTypeOptions(); RefreshComponentView(); } } }
    public string SelectedComponentType { get => _selectedComponentType; set { if (SetProperty(ref _selectedComponentType, value)) RefreshComponentView(); } }
    public string SelectedClientUniverse { get => _selectedClientUniverse; set { if (SetProperty(ref _selectedClientUniverse, string.IsNullOrWhiteSpace(value) ? "Tous les univers" : value)) RefreshClientFurnitureView(); } }
    public string SelectedClientType { get => _selectedClientType; set { if (SetProperty(ref _selectedClientType, string.IsNullOrWhiteSpace(value) ? "Tous les types" : value)) RefreshClientFurnitureView(); } }
    public string SelectedClientFamily { get => _selectedClientFamily; set { if (SetProperty(ref _selectedClientFamily, string.IsNullOrWhiteSpace(value) ? "Toutes les familles" : value)) RefreshClientFurnitureView(); } }
    public string SelectedClientTag { get => _selectedClientTag; set { if (SetProperty(ref _selectedClientTag, string.IsNullOrWhiteSpace(value) ? "Tous les tags" : value)) RefreshClientFurnitureView(); } }
    public string CurrentFurnitureStep
    {
        get => _currentFurnitureStep;
        set
        {
            if (!SetProperty(ref _currentFurnitureStep, value)) return;
            foreach (var step in FurnitureSteps) step.IsActive = step.Key.Equals(value, StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(FurnitureStepIndicator));
            OnPropertyChanged(nameof(IsReviewStep));
            PreviousFurnitureStepCommand.RaiseCanExecuteChanged();
            NextFurnitureStepCommand.RaiseCanExecuteChanged();
        }
    }
    public string FurnitureStepIndicator => $"Étape {Math.Max(1, FurnitureSteps.ToList().FindIndex(x => x.Key.Equals(CurrentFurnitureStep, StringComparison.OrdinalIgnoreCase)) + 1)} sur 5";
    public bool IsReviewStep => CurrentFurnitureStep == "Review";
    public int CompositionTotalQuantity => CompositionLines.Sum(x => x.Quantity);
    public int MarkedCompositionLineCount => _selectedCompositionLines.Count;
    public string RemoveCompositionLabel => MarkedCompositionLineCount == 0 ? "Sélectionnez une ou plusieurs lignes" : $"Retirer la sélection ({MarkedCompositionLineCount})";
    public int VisibleFurnitureCount => FurnitureView.Cast<object>().Count();
    public string CreationMode { get => _creationMode; set { if (SetProperty(ref _creationMode, value)) OnPropertyChanged(nameof(IsFamilyMode)); } }
    public string NewUniverseName { get => _newUniverseName; set { if (SetProperty(ref _newUniverseName, value)) AddUniverseCommand.RaiseCanExecuteChanged(); } }
    public LibraryFilterViewModel? SelectedLibraryFilter
    {
        get => _selectedLibraryFilter;
        set
        {
            if (!SetProperty(ref _selectedLibraryFilter, value)) return;
            ActiveFamilyFilter = "Toutes";
            RebuildScopedComponentFilters();
            RefreshComponentView();
        }
    }
    public FurnitureFamilyRecord? SelectedFurnitureFamily { get => _selectedFurnitureFamily; set => SetProperty(ref _selectedFurnitureFamily, value); }
    public ComponentCardViewModel? SelectedComponentCard { get => _selectedComponentCard; set { if (SetProperty(ref _selectedComponentCard, value)) { OnPropertyChanged(nameof(SelectedComponent)); RaiseCommandStates(); } } }
    public ComponentRecord? SelectedComponent => SelectedComponentCard?.Record;
    public FurnitureRecord? SelectedFurniture
    {
        get => _selectedFurniture;
        set
        {
            if (_selectedFurniture is not null) _selectedFurniture.PropertyChanged -= SelectedFurnitureOnPropertyChanged;
            if (!SetProperty(ref _selectedFurniture, value)) return;
            if (_selectedFurniture is not null) _selectedFurniture.PropertyChanged += SelectedFurnitureOnPropertyChanged;
            RefreshLinkedComponents(); RebuildUniverseOptions(); RebuildUsageOptions(); LoadFurniturePreview(); RaiseCommandStates();
        }
    }
    public ComponentRecord? SelectedCompositionCandidate { get => _selectedCompositionCandidate; set { if (SetProperty(ref _selectedCompositionCandidate, value)) RaiseCommandStates(); } }
    public ComponentRecord? SelectedLinkedComponent { get => _selectedLinkedComponent; set { if (SetProperty(ref _selectedLinkedComponent, value)) RaiseCommandStates(); } }
    public FurnitureCardViewModel? SelectedClientFurnitureCard
    {
        get => _selectedClientFurnitureCard;
        set
        {
            if (!SetProperty(ref _selectedClientFurnitureCard, value)) return;
            OnPropertyChanged(nameof(SelectedClientFurniture));
            OnPropertyChanged(nameof(SelectedClientTags));
            OnPropertyChanged(nameof(SelectedClientUses));
            OnPropertyChanged(nameof(SelectedClientStructure));
        }
    }
    public FurnitureRecord? SelectedClientFurniture => SelectedClientFurnitureCard?.Record;
    public string SelectedClientTags => SelectedClientFurniture is null ? "Aucun tag" : string.Join(" · ", ComponentTaxonomyStore.Resolve(SelectedClientFurniture, Components, _taxonomy).Select(tag => tag.Label));
    public string SelectedClientUses => SelectedClientFurniture is null ? string.Empty : string.Join(" · ", FurnitureUsagesFor(SelectedClientFurniture));
    public string SelectedClientStructure => SelectedClientFurniture is null ? string.Empty : string.Join(" · ", new[] { SelectedClientFurniture.PrincipleConstruction, SelectedClientFurniture.TypeAssemblage, SelectedClientFurniture.PositionDos }.Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("Non applicable", StringComparison.OrdinalIgnoreCase)));
    public string InheritedTags => SelectedFurniture is null ? string.Empty : string.Join(" · ", ComponentTaxonomyStore.Resolve(SelectedFurniture, Components, _taxonomy).Select(x => x.Label));
    public string InheritedTagsByCategory => SelectedFurniture is null ? string.Empty : string.Join(Environment.NewLine, ComponentTaxonomyStore.Resolve(SelectedFurniture, Components, _taxonomy).GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Autre" : x.Category).OrderBy(x => x.Key).Select(group => $"{group.Key} : {string.Join(", ", group.Select(x => x.Label).Distinct(StringComparer.OrdinalIgnoreCase))}"));
    public string InheritedFamilies => string.Join(" · ", LinkedComponents.Select(x => x.EffectiveFamilyName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase));
    public string InheritedCompatibility => JoinInherited(item => item.CompatibilityCsv);
    public BitmapImage? FurniturePreview { get => _furniturePreview; private set => SetProperty(ref _furniturePreview, value); }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand ToggleNavigationCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand ChooseLibraryCommand { get; }
    public RelayCommand ChooseSharedRootCommand { get; }
    public RelayCommand SaveBootstrapCommand { get; }
    public RelayCommand ValidateComponentCommand { get; }
    public RelayCommand SetComponentLayoutCommand { get; }
    public RelayCommand SetFamilyFilterCommand { get; }
    public RelayCommand ClearComponentFiltersCommand { get; }
    public RelayCommand SelectVisibleComponentsCommand { get; }
    public RelayCommand ClearMarkedComponentsCommand { get; }
    public RelayCommand ClearClientFiltersCommand { get; }
    public RelayCommand ToggleClientFiltersCommand { get; }
    public RelayCommand ClearClientSelectionCommand { get; }
    public RelayCommand PrepareTopSolidBridgeCommand { get; }
    public RelayCommand OpenTopSolidBridgeFolderCommand { get; }
    public RelayCommand CreateFurnitureCommand { get; }
    public RelayCommand CreateFamilyCommand { get; }
    public RelayCommand SetCreationModeCommand { get; }
    public RelayCommand NavigateFurnitureStepCommand { get; }
    public RelayCommand PreviousFurnitureStepCommand { get; }
    public RelayCommand NextFurnitureStepCommand { get; }
    public RelayCommand ChooseFurnitureTopCommand { get; }
    public RelayCommand OpenFurnitureFolderCommand { get; }
    public RelayCommand AddUniverseCommand { get; }
    public RelayCommand AddComponentCommand { get; }
    public RelayCommand AddMarkedComponentsCommand { get; }
    public RelayCommand RemoveComponentCommand { get; }
    public RelayCommand RemoveMarkedCompositionCommand { get; }
    public RelayCommand PublishFurnitureCommand { get; }
    public RelayCommand OpenSharedRootCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }

    public async Task InitializeAsync()
    {
        try { _horizonPreferences = await _horizonPreferencesStore.LoadAsync(); }
        catch { _horizonPreferences = new HorizonPreferences(); }
        await ReloadAsync(false);
        foreach (var user in await _userStore.LoadAsync()) Users.Add(user);
    }

    public async Task CheckAutoUpdateAsync() { if (Settings.AutoUpdate) await CheckUpdateAsync(true); }

    public async Task AddUserAsync(string login, string displayName, string password, string roleProfile)
    {
        if (!IsAdministrator) throw new UnauthorizedAccessException("Seul un administrateur peut créer un compte.");
        var permissions = roleProfile switch
        {
            "Éditeur" => UserPermissions.Read | UserPermissions.Edit,
            "Éditeur + validateur" => UserPermissions.Read | UserPermissions.Edit | UserPermissions.Validate,
            "Administrateur" => UserPermissions.Read | UserPermissions.Edit | UserPermissions.Validate | UserPermissions.Administer,
            _ => UserPermissions.Read
        };
        var account = await _userStore.AddAsync(login, displayName, password, permissions); Users.Add(account);
    }

    private async Task ReloadAsync(bool showMessage = true)
    {
        IsBusy = true;
        try
        {
            _catalog = await _store.LoadAsync(); _taxonomy = await ComponentTaxonomyStore.LoadAsync(SharedRoot); NormalizeCatalog();
            Components.Clear(); foreach (var item in _catalog.Components) Components.Add(item);
            Furniture.Clear(); foreach (var item in _catalog.Furniture) Furniture.Add(item);
            FurnitureFamilies.Clear(); foreach (var item in _catalog.FurnitureFamilies) FurnitureFamilies.Add(item);
            RebuildComponentCards(); RebuildClientCards();
            SelectedComponentCard = ComponentCards.FirstOrDefault(); SelectedFurniture = Furniture.FirstOrDefault(); SelectedFurnitureFamily = FurnitureFamilies.FirstOrDefault();
            SelectedClientFurnitureCard = ClientFurnitureCards.FirstOrDefault(item => item.Record.Status == RecordStatus.Publiee);
            NotifySummary(); StatusText = showMessage ? "Catalogue rechargé." : LastModification;
        }
        catch (Exception exception) { ShowError(exception); }
        finally { IsBusy = false; }
    }

    private void NormalizeCatalog()
    {
        _catalog.Settings ??= new(); _catalog.Components ??= []; _catalog.Furniture ??= []; _catalog.FurnitureFamilies ??= []; _catalog.Universes ??= []; _catalog.UniverseDefinitions ??= [];
        _catalog.Settings.SearchSynonyms ??= [];
        if (!_catalog.Settings.SearchDictionaryInitialized)
        {
            _catalog.Settings.SearchSynonyms.AddRange(DefaultSearchSynonyms());
            _catalog.Settings.SearchDictionaryInitialized = true;
        }
        if (_catalog.Universes.Count == 0) _catalog.Universes.AddRange(DefaultUniverses);
        if (_catalog.UniverseDefinitions.Count == 0)
        {
            _catalog.UniverseDefinitions.AddRange(_catalog.Universes.Select((name, index) => new CatalogUniverseRecord
            {
                Name = name,
                SortOrder = index,
                Description = DefaultUniverseDescription(name)
            }));
        }
        foreach (var legacyName in _catalog.Universes.Where(name => !_catalog.UniverseDefinitions.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
            _catalog.UniverseDefinitions.Add(new CatalogUniverseRecord { Name = legacyName, SortOrder = _catalog.UniverseDefinitions.Count, Description = DefaultUniverseDescription(legacyName) });
        NormalizeUniverseDefinitions();
        foreach (var item in _catalog.Components) item.NormalizeTags();
        foreach (var item in _catalog.Furniture)
        {
            item.Universes ??= []; item.Usages ??= []; item.ComponentIds ??= []; item.ComponentLines ??= []; item.AddedTagIds ??= []; item.RemovedInheritedTagIds ??= [];
            if (item.ComponentLines.Count == 0)
                item.ComponentLines.AddRange(item.ComponentIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => new FurnitureComponentLine { ComponentId = id, Quantity = 1 }));
            SyncLegacyComponentIds(item);
            if (item.Usages.Count == 0 && !string.IsNullOrWhiteSpace(item.UsageSpecifique))
                item.Usages.AddRange(item.UsageSpecifique.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            item.ConceptionDate ??= DateTime.Today;
        }
        _catalog.SchemaVersion = Math.Max(_catalog.SchemaVersion, 6);
    }

    private async Task<bool> SaveAsync()
    {
        IsBusy = true;
        try
        {
            _catalog.Components = Components.ToList(); _catalog.Furniture = Furniture.ToList(); _catalog.FurnitureFamilies = FurnitureFamilies.ToList();
            NormalizeUniverseDefinitions();
            await _store.SaveAsync(_catalog, _catalog.Revision, CurrentUser.DisplayName); StatusText = $"Enregistré · révision {_catalog.Revision}"; NotifySummary();
            return true;
        }
        catch (CatalogConcurrencyException exception) { AtlasDialog.Warning(exception.Message, "Modification concurrente"); return false; }
        catch (Exception exception) { ShowError(exception); return false; }
        finally { IsBusy = false; }
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.LibraryRoot)) ChooseLibrary();
        if (string.IsNullOrWhiteSpace(Settings.LibraryRoot)) return;
        IsBusy = true; StatusText = "Analyse de la bibliothèque…";
        try
        {
            var scan = await _scanner.ScanAsync(Settings.LibraryRoot);
            foreach (var existing in Components.Where(item => !item.IsDemo)) existing.IsMissing = true;
            if (scan.Components.Count > 0) { foreach (var item in Components.Where(x => x.IsDemo).ToArray()) Components.Remove(item); foreach (var item in Furniture.Where(x => x.IsDemo).ToArray()) Furniture.Remove(item); }
            foreach (var item in scan.Components)
            {
                var record = Components.FirstOrDefault(component => component.Id == item.StableId);
                if (record is null)
                {
                    record = new() { Id = item.StableId, DisplayName = ComponentNameParser.SuggestDisplayName(item.TechnicalName), Status = RecordStatus.Brouillon };
                    Components.Add(record);
                }
                else if (string.IsNullOrWhiteSpace(record.DisplayName) || record.DisplayName.Equals(record.TechnicalName, StringComparison.OrdinalIgnoreCase) || record.DisplayName.Equals(ComponentNameParser.SuggestDisplayName(record.TechnicalName), StringComparison.OrdinalIgnoreCase))
                {
                    record.DisplayName = ComponentNameParser.SuggestDisplayName(item.TechnicalName);
                }
                record.SourceRelativePath = item.RelativeTopPath; record.PreviewRelativePath = item.PreviewRelativePath; record.LibraryName = item.Library; record.FamilyName = item.Family; record.TechnicalName = item.TechnicalName;
                record.TypeCode = item.Parsed.Type; record.VariantCode = item.Parsed.Variant; record.IndexCode = item.Parsed.Index; record.RangeCode = item.Parsed.Range; record.ConstructionCode = item.Parsed.Construction;
                record.IsNameCompliant = item.IsCompliant; record.IsMissing = false; record.LastSeenUtc = DateTimeOffset.UtcNow;
            }
            var taxonomy = await ComponentTaxonomyStore.LoadAsync(SharedRoot);
            var addedFamilies = ComponentTaxonomyStore.SyncDetectedFamilies(taxonomy, Components);
            await ComponentTaxonomyStore.SaveAsync(SharedRoot, taxonomy);
            RebuildComponentCards(); RebuildClientCards(); NotifySummary(); StatusText = $"{scan.Components.Count:N0} composants indexés · {scan.Warnings.Count} avertissement(s).";
            if (addedFamilies > 0) StatusText += $" · {addedFamilies} nouvelle(s) famille(s) détectée(s).";
        }
        catch (Exception exception) { ShowError(exception); }
        finally { IsBusy = false; }
    }

    private void RebuildComponentCards()
    {
        ComponentCards.Clear();
        foreach (var record in Components) { var card = new ComponentCardViewModel(record, Settings.LibraryRoot); card.PropertyChanged += ComponentCardOnPropertyChanged; ComponentCards.Add(card); }
        RebuildComponentFilters(); ComponentView.Refresh();
    }

    private void RebuildComponentFilters()
    {
        var previous = SelectedLibraryFilter?.Name; LibraryFilters.Clear();
        LibraryFilters.Add(new() { Name = "Toutes les bibliothèques", TotalCount = ComponentCards.Count });
        foreach (var group in ComponentCards.GroupBy(item => NormalizeBucket(item.Library)).OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)) LibraryFilters.Add(new() { Name = group.Key, TotalCount = group.Count() });
        SelectedLibraryFilter = LibraryFilters.FirstOrDefault(item => item.Name == previous) ?? LibraryFilters.FirstOrDefault();
        RebuildScopedComponentFilters();
    }

    private void RebuildScopedComponentFilters()
    {
        var scoped = ScopedComponentCards().ToArray();
        FamilyFilters.Clear(); FamilyFilters.Add(new() { Label = "Toutes", Count = scoped.Length, IsActive = ActiveFamilyFilter == "Toutes" });
        foreach (var group in scoped.GroupBy(item => NormalizeBucket(item.Family)).OrderByDescending(item => item.Count()).ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)) FamilyFilters.Add(new() { Label = group.Key, Count = group.Count(), IsActive = ActiveFamilyFilter == group.Key });
        if (!FamilyFilters.Any(x => x.Label.Equals(ActiveFamilyFilter, StringComparison.OrdinalIgnoreCase))) ActiveFamilyFilter = "Toutes";
        RebuildComponentTypeOptions();
    }

    private void RebuildComponentTypeOptions()
    {
        var scoped = ScopedComponentCards();
        if (ActiveFamilyFilter != "Toutes") scoped = scoped.Where(item => NormalizeBucket(item.Family) == ActiveFamilyFilter);
        ComponentTypeOptions.Clear();
        ComponentTypeOptions.Add("Tous les types");
        foreach (var type in scoped.Select(item => item.Type).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)) ComponentTypeOptions.Add(type);
        if (!ComponentTypeOptions.Contains(SelectedComponentType)) SelectedComponentType = "Tous les types";
    }

    private IEnumerable<ComponentCardViewModel> ScopedComponentCards() =>
        SelectedLibraryFilter is { Name: not "Toutes les bibliothèques" }
            ? ComponentCards.Where(item => NormalizeBucket(item.Library) == SelectedLibraryFilter.Name)
            : ComponentCards;

    private void RebuildClientCards()
    {
        ClientFurnitureCards.Clear();
        foreach (var item in Furniture)
        {
            var card = new FurnitureCardViewModel(item, Settings.LibraryRoot);
            card.PropertyChanged += ClientFurnitureCardOnPropertyChanged;
            ClientFurnitureCards.Add(card);
        }
        InvalidateTopSolidBridge();
        NotifyClientSelectionChanged();
        RebuildClientFacets();
        RefreshClientFurnitureView();
        if (SelectedClientFurnitureCard is null || !ClientFurnitureView.Cast<FurnitureCardViewModel>().Contains(SelectedClientFurnitureCard))
            SelectedClientFurnitureCard = ClientFurnitureView.Cast<FurnitureCardViewModel>().FirstOrDefault();
    }

    private void RebuildClientFacets()
    {
        _suppressClientFacetRefresh = true;
        try
        {
            var published = Furniture.Where(item => item.Status == RecordStatus.Publiee).ToArray();
            BuildUniverseFacets(published);
            BuildClientFacet("Types", ClientTypeFacets, CountValues(published.Select(item => item.TypeMeuble)));
            BuildClientFacet("Usages", ClientUsageFacets, CountValues(published.SelectMany(FurnitureUsagesFor)));
            BuildClientFacet("Familles", ClientFamilyFacets, CountValues(published.Select(item => item.Family)));
            BuildClientFacet("Formes", ClientFormFacets, CountValues(published.Select(item => item.Forme)));
            BuildClientFacet("Construction", ClientConstructionFacets, CountValues(published.Select(item => item.PrincipleConstruction)));
            BuildClientFacet("Assemblage", ClientAssemblyFacets, CountValues(published.Select(item => item.TypeAssemblage)));
            BuildClientFacet("Dos", ClientBackFacets, CountValues(published.Select(item => item.PositionDos)));
            BuildClientFacet("Tags", ClientTagFacets, CountValues(published.SelectMany(item => ComponentTaxonomyStore.Resolve(item, Components, _taxonomy).Select(tag => tag.Label).Distinct(StringComparer.OrdinalIgnoreCase))));
            UpdateFavoriteFacets();
        }
        finally { _suppressClientFacetRefresh = false; }
    }

    private void BuildUniverseFacets(IReadOnlyCollection<FurnitureRecord> published)
    {
        var selected = ClientUniverseFacets.Where(option => option.IsSelected).Select(option => option.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ClientUniverseFacets.Clear();
        foreach (var universe in _catalog.UniverseDefinitions.Where(item => item.IsActive)
                     .OrderByDescending(item => _horizonPreferences.FavoriteFacetKeys.Contains(FavoriteFacetKey("Univers", item.Name)))
                     .ThenBy(item => item.SortOrder)
                     .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var count = published.Count(item => item.Universes.Contains(universe.Name, StringComparer.OrdinalIgnoreCase));
            var option = new CatalogFacetViewModel("Univers", universe.Name, count, _ => ClientFacetChanged(), universe.Description, LoadUniverseImage(universe))
            {
                IsFavorite = _horizonPreferences.FavoriteFacetKeys.Contains(FavoriteFacetKey("Univers", universe.Name))
            };
            ClientUniverseFacets.Add(option);
            if (selected.Contains(universe.Name)) option.IsSelected = true;
        }
    }

    private BitmapImage? LoadUniverseImage(CatalogUniverseRecord universe)
    {
        if (string.IsNullOrWhiteSpace(universe.ImageRelativePath)) return null;
        var path = Path.IsPathRooted(universe.ImageRelativePath)
            ? universe.ImageRelativePath
            : Path.Combine(SharedRoot, universe.ImageRelativePath);
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 420;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private void BuildClientFacet(string group, ObservableCollection<CatalogFacetViewModel> target, IEnumerable<(string Label, int Count)> values)
    {
        var selected = target.Where(option => option.IsSelected).Select(option => option.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var (label, count) in values.Where(item => !string.IsNullOrWhiteSpace(item.Label) && item.Count > 0)
                     .OrderByDescending(item => _horizonPreferences.FavoriteFacetKeys.Contains(FavoriteFacetKey(group, item.Label)))
                     .ThenByDescending(item => item.Count)
                     .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var option = new CatalogFacetViewModel(group, label, count, _ => ClientFacetChanged())
            {
                IsFavorite = _horizonPreferences.FavoriteFacetKeys.Contains(FavoriteFacetKey(group, label))
            };
            target.Add(option);
            if (selected.Contains(label)) option.IsSelected = true;
        }
    }

    public void ToggleClientFavorite(CatalogFacetViewModel option)
    {
        option.IsFavorite = !option.IsFavorite;
        if (option.IsFavorite) _horizonPreferences.FavoriteFacetKeys.Add(option.FavoriteKey);
        else _horizonPreferences.FavoriteFacetKeys.Remove(option.FavoriteKey);
        RebuildClientFacets();
        RefreshClientFurnitureView();
        _ = SaveHorizonPreferencesAsync();
    }

    private async Task SaveHorizonPreferencesAsync()
    {
        try { await _horizonPreferencesStore.SaveAsync(_horizonPreferences); }
        catch (Exception exception) { StatusText = $"Favoris non enregistrés : {exception.Message}"; }
    }

    private void UpdateFavoriteFacets()
    {
        ClientFavoriteFacets.Clear();
        foreach (var option in ClientFacetGroups().SelectMany(group => group).Where(option => option.IsFavorite)
                     .OrderBy(option => option.Group, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase))
            ClientFavoriteFacets.Add(option);
        OnPropertyChanged(nameof(HasClientFavorites));
    }

    private static string FavoriteFacetKey(string group, string label) => $"{group}|{CatalogSearchEngine.Normalize(label)}";

    private static IEnumerable<(string Label, int Count)> CountValues(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, group.Count()));

    private static IEnumerable<string> FurnitureUsagesFor(FurnitureRecord item) =>
        (item.Usages ?? []).Concat(string.IsNullOrWhiteSpace(item.UsageSpecifique) ? [] : [item.UsageSpecifique])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<ObservableCollection<CatalogFacetViewModel>> ClientFacetGroups()
    {
        yield return ClientUniverseFacets;
        yield return ClientTypeFacets;
        yield return ClientUsageFacets;
        yield return ClientFamilyFacets;
        yield return ClientTagFacets;
        yield return ClientFormFacets;
        yield return ClientConstructionFacets;
        yield return ClientAssemblyFacets;
        yield return ClientBackFacets;
    }

    private void ClientFacetChanged()
    {
        if (!_suppressClientFacetRefresh) RefreshClientFurnitureView();
    }

    private void ClearClientFilters()
    {
        _suppressClientFacetRefresh = true;
        try
        {
            foreach (var option in ClientFacetGroups().SelectMany(group => group)) option.IsSelected = false;
            ClientSearch = string.Empty;
        }
        finally { _suppressClientFacetRefresh = false; }
        RefreshClientFurnitureView();
    }

    private void ClientFurnitureCardOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FurnitureCardViewModel.IsChosen)) return;
        InvalidateTopSolidBridge();
        NotifyClientSelectionChanged();
    }

    private void NotifyClientSelectionChanged()
    {
        OnPropertyChanged(nameof(ClientSelectionCount));
        OnPropertyChanged(nameof(ClientSelectionLabel));
        ClearClientSelectionCommand.RaiseCanExecuteChanged();
        PrepareTopSolidBridgeCommand.RaiseCanExecuteChanged();
    }

    private void ClearClientSelection()
    {
        foreach (var card in ClientFurnitureCards.Where(item => item.IsChosen).ToArray()) card.IsChosen = false;
        InvalidateTopSolidBridge();
        NotifyClientSelectionChanged();
    }

    private void PrepareTopSolidBridge()
    {
        var selected = ClientFurnitureCards.Where(item => item.IsChosen).ToArray();
        if (selected.Length == 0)
        {
            AtlasDialog.Warning("Sélectionnez au moins un meuble dans Horizon.", "Passerelle TopSolid");
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de travail pour ce transfert",
            Multiselect = false
        };
        if (Directory.Exists(TopSolidBridgeFolder)) dialog.InitialDirectory = TopSolidBridgeFolder;
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var sourceFiles = selected.Select(card => (card.DisplayName, Path: ResolveLibraryPath(card.Record.SourceRelativePath))).ToArray();
            var missing = sourceFiles.Where(item => string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path)).Select(item => item.DisplayName).ToArray();
            if (missing.Length > 0)
            {
                AtlasDialog.Warning(
                    "La passerelle ne peut pas être préparée car certains fichiers .TOP sont introuvables.",
                    "Fichiers manquants",
                    string.Join(Environment.NewLine, missing.Take(8)) + (missing.Length > 8 ? $"{Environment.NewLine}… et {missing.Length - 8} autre(s)" : string.Empty));
                return;
            }

            Directory.CreateDirectory(dialog.FolderName);
            var copies = new List<string>();
            foreach (var source in sourceFiles)
            {
                var destination = NextAvailableFileName(dialog.FolderName, Path.GetFileName(source.Path));
                File.Copy(source.Path, destination, false);
                copies.Add(destination);
            }

            TopSolidBridgeFolder = dialog.FolderName;
            TopSolidBridgeFiles = copies;
            IsTopSolidBridgeReady = copies.Count > 0;
            OnPropertyChanged(nameof(TopSolidBridgeCountLabel));
            OpenTopSolidBridgeFolderCommand.RaiseCanExecuteChanged();
            StatusText = $"Passerelle prête · {copies.Count} copie(s) créée(s) dans {dialog.FolderName}";
        }
        catch (Exception exception)
        {
            InvalidateTopSolidBridge();
            ShowError(exception);
        }
        finally { IsBusy = false; }
    }

    private static string NextAvailableFileName(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(folder, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private void InvalidateTopSolidBridge()
    {
        IsTopSolidBridgeReady = false;
        TopSolidBridgeFiles = Array.Empty<string>();
        OnPropertyChanged(nameof(TopSolidBridgeCountLabel));
        OpenTopSolidBridgeFolderCommand.RaiseCanExecuteChanged();
    }

    private void OpenTopSolidBridgeFolder()
    {
        try
        {
            if (!Directory.Exists(TopSolidBridgeFolder))
            {
                AtlasDialog.Warning("Le dossier préparé n’existe plus.", "Passerelle TopSolid");
                InvalidateTopSolidBridge();
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", TopSolidBridgeFolder) { UseShellExecute = true });
        }
        catch (Exception exception) { ShowError(exception); }
    }

    public void CompleteTopSolidBridgeDrop()
    {
        StatusText = $"Transfert remis à TopSolid · {TopSolidBridgeFiles.Count} fichier(s).";
        ClearClientSelection();
    }

    private void ComponentCardOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComponentCardViewModel.IsMarked)) return;
        foreach (var filter in LibraryFilters) filter.MarkedCount = filter.Name == "Toutes les bibliothèques" ? ComponentCards.Count(item => item.IsMarked) : ComponentCards.Count(item => item.IsMarked && NormalizeBucket(item.Library) == filter.Name);
        OnPropertyChanged(nameof(LibraryFilters)); OnPropertyChanged(nameof(MarkedComponentCount)); AddMarkedComponentsCommand.RaiseCanExecuteChanged();
    }

    private void RefreshComponentView() { ComponentView.Refresh(); OnPropertyChanged(nameof(VisibleComponentLabel)); }
    private void RefreshFurnitureView() { FurnitureView.Refresh(); OnPropertyChanged(nameof(VisibleFurnitureCount)); }
    private void RefreshClientFurnitureView()
    {
        UpdateClientSearchScores();
        ClientFurnitureView.Refresh();
        OnPropertyChanged(nameof(PublishedCount));
        OnPropertyChanged(nameof(ClientResultCount));
        OnPropertyChanged(nameof(HasClientResults));
        OnPropertyChanged(nameof(ClientResultLabel));
        OnPropertyChanged(nameof(ActiveClientFilterCount));
        OnPropertyChanged(nameof(ClientFilterButtonLabel));
        if (SelectedClientFurnitureCard is null || !ClientFurnitureView.Cast<FurnitureCardViewModel>().Contains(SelectedClientFurnitureCard))
            SelectedClientFurnitureCard = ClientFurnitureView.Cast<FurnitureCardViewModel>().FirstOrDefault();
    }

    private void UpdateClientSearchScores()
    {
        if (string.IsNullOrWhiteSpace(ClientSearch))
        {
            foreach (var card in ClientFurnitureCards) card.SearchScore = 0d;
            ClientSearchInterpretation = string.Empty;
        }
        else
        {
            foreach (var card in ClientFurnitureCards)
                card.SearchScore = CatalogSearchEngine.Score(ClientSearch, SearchFieldsFor(card.Record), Settings.SearchSynonyms);
            var corrected = CatalogSearchEngine.CorrectQuery(ClientSearch, ClientSearchVocabulary(), Settings.SearchSynonyms);
            var normalized = CatalogSearchEngine.Normalize(ClientSearch);
            ClientSearchInterpretation = corrected.Equals(normalized, StringComparison.Ordinal) ? string.Empty : $"Atlas a compris : {corrected}";
        }
        OnPropertyChanged(nameof(ClientSearchInterpretation));
        OnPropertyChanged(nameof(HasClientSearchInterpretation));
    }

    private IEnumerable<CatalogSearchField> SearchFieldsFor(FurnitureRecord furniture)
    {
        var tags = string.Join(' ', ComponentTaxonomyStore.Resolve(furniture, Components, _taxonomy).Select(tag => tag.Label));
        yield return new CatalogSearchField(furniture.DisplayName, 1d);
        yield return new CatalogSearchField(furniture.Reference, 0.85d);
        yield return new CatalogSearchField($"{furniture.TypeMeuble} {furniture.Family} {string.Join(' ', FurnitureUsagesFor(furniture))} {tags}", 0.95d);
        yield return new CatalogSearchField($"{string.Join(' ', furniture.Universes)} {furniture.Forme}", 0.72d);
        yield return new CatalogSearchField($"{furniture.PrincipleConstruction} {furniture.TypeAssemblage} {furniture.PositionDos}", 0.72d);
        yield return new CatalogSearchField(furniture.Description, 0.58d);
    }

    private IEnumerable<string> ClientSearchVocabulary() => Furniture.Where(item => item.Status == RecordStatus.Publiee).SelectMany(item =>
        SearchFieldsFor(item).Select(field => field.Text)).Concat(Settings.SearchSynonyms.SelectMany(item => new[] { item.Canonical, item.AliasesCsv }));

    public void RefreshHorizonSearchSettings()
    {
        RebuildClientFacets();
        RefreshClientFurnitureView();
    }

    private void NormalizeUniverseDefinitions()
    {
        _catalog.UniverseDefinitions ??= [];
        var cleaned = _catalog.UniverseDefinitions
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (var index = 0; index < cleaned.Count; index++)
        {
            cleaned[index].Name = cleaned[index].Name.Trim();
            cleaned[index].Description = string.IsNullOrWhiteSpace(cleaned[index].Description)
                ? DefaultUniverseDescription(cleaned[index].Name)
                : cleaned[index].Description.Trim();
            cleaned[index].SortOrder = index;
        }
        _catalog.UniverseDefinitions = cleaned;
        _catalog.Universes = cleaned.Where(item => item.IsActive).Select(item => item.Name).ToList();
    }

    private static string DefaultUniverseDescription(string name) => name switch
    {
        "Cuisine" => "Préparation, cuisson et rangement",
        "Dressing" => "Vestiaires et rangements sur mesure",
        "Salle de bain" => "Meubles vasque et rangements d’eau",
        "Bibliothèque" => "Livres, objets et compositions murales",
        "Séjour" => "Mobilier de vie et rangements",
        "Bureau / Tertiaire" => "Postes de travail et espaces professionnels",
        "Buanderie" => "Entretien et rangements techniques",
        "Agencement commercial" => "Accueil, présentation et vente",
        "Chambre" => "Couchage et rangements privés",
        "Hôtellerie / Hébergement" => "Accueil et mobilier d’hébergement",
        "Restaurant / Bar" => "Service, convivialité et restauration",
        _ => "Collection de meubles dédiée"
    };

    private static List<SearchSynonymRecord> DefaultSearchSynonyms() =>
    [
        new() { Canonical = "meuble", AliasesCsv = "mobilier, caisson" },
        new() { Canonical = "sous évier", AliasesCsv = "évier, lavabo, meuble évier" },
        new() { Canonical = "tiroir", AliasesCsv = "coulissant, rangement coulissant" },
        new() { Canonical = "porte", AliasesCsv = "façade, ouvrant" },
        new() { Canonical = "charnière", AliasesCsv = "façade battante, porte battante" },
        new() { Canonical = "réfrigérateur", AliasesCsv = "frigo" },
        new() { Canonical = "poubelle", AliasesCsv = "déchets, tri sélectif" },
        new() { Canonical = "four", AliasesCsv = "cuisson" }
    ];
    private void ClearComponentFilters() { ComponentSearch = ""; ActiveFamilyFilter = "Toutes"; SelectedComponentType = "Tous les types"; SelectedLibraryFilter = LibraryFilters.FirstOrDefault(); }
    private void MarkVisibleComponents(bool marked) { foreach (var card in ComponentView.Cast<ComponentCardViewModel>().ToArray()) card.IsMarked = marked; }
    private void UpdateFamilyFilterStates() { foreach (var filter in FamilyFilters) filter.IsActive = filter.Label == ActiveFamilyFilter; OnPropertyChanged(nameof(FamilyFilters)); }

    private void ValidateComponent()
    {
        if (SelectedComponent is null) return;
        if (!SelectedComponent.IsNameCompliant && string.IsNullOrWhiteSpace(SelectedComponent.ForcedValidationReason)) { AtlasDialog.Warning("Ce nom n’est pas conforme. Renseignez le motif de validation forcée avant de valider.", "Validation"); return; }
        SelectedComponent.NormalizeTags();
        SelectedComponent.Status = RecordStatus.Validee; SelectedComponent.ValidatedBy = CurrentUser.DisplayName; SelectedComponent.ValidatedUtc = DateTimeOffset.UtcNow;
        StatusText = $"Fiche validée par {CurrentUser.DisplayName}. Pensez à enregistrer."; RefreshComponentView();
    }

    private void CreateFurniture()
    {
        var family = CreationMode == "Family" ? SelectedFurnitureFamily : null;
        var item = new FurnitureRecord
        {
            Reference = NextFurnitureReference(), DisplayName = family is null ? "Nouveau meuble" : $"{family.Name} · nouvelle variante",
            Family = family?.Name ?? "", FamilyId = family?.Id ?? "", Description = family?.Description ?? "", TypeMeuble = family?.TypeMeuble ?? "", UsageSpecifique = family?.UsageSpecifique ?? "", Forme = family?.Forme ?? "Droit",
            Universes = family?.Universes.ToList() ?? [], Usages = string.IsNullOrWhiteSpace(family?.UsageSpecifique) ? [] : [family.UsageSpecifique], ConceptionDate = DateTime.Today, Status = RecordStatus.Brouillon
        };
        Furniture.Add(item); SelectedFurniture = item; FurnitureView.Refresh(); RebuildClientCards(); CurrentPage = "Furniture"; CurrentFurnitureStep = "Identity"; NotifySummary();
    }

    private string NextFurnitureReference()
    {
        var used = Furniture.Select(value => value.Reference?.Trim() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var maximum = Furniture
            .Select(value => value.Reference?.Trim())
            .Where(value => value is { Length: 9 } && value.All(char.IsDigit))
            .Select(value => int.TryParse(value, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        for (var number = maximum + 1; number <= 999_999_999; number++)
        {
            var candidate = number.ToString("D9");
            if (!used.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Le compteur des références Atlas a atteint sa limite.");
    }

    private void CreateFamily()
    {
        var family = new FurnitureFamilyRecord { Name = "Nouvelle famille", CreatedBy = CurrentUser.DisplayName };
        FurnitureFamilies.Add(family); SelectedFurnitureFamily = family; CreationMode = "Family"; StatusText = "Famille créée. Renseignez ses données communes, puis créez ses variantes.";
    }

    private void RebuildUniverseOptions()
    {
        UniverseOptions.Clear(); var selected = SelectedFurniture?.Universes ?? [];
        foreach (var universe in _catalog.Universes) UniverseOptions.Add(new(universe, selected.Contains(universe, StringComparer.OrdinalIgnoreCase), ToggleUniverse));
        RebuildFurnitureTagOptions();
    }

    private void RebuildUsageOptions()
    {
        UsageOptions.Clear();
        var selected = SelectedFurniture?.Usages ?? [];
        foreach (var usage in FurnitureUsages) UsageOptions.Add(new(usage, selected.Contains(usage, StringComparer.OrdinalIgnoreCase), ToggleUsage));
    }

    private void ToggleUsage(ToggleOptionViewModel option)
    {
        if (SelectedFurniture is null) return;
        SelectedFurniture.Usages ??= [];
        var existing = SelectedFurniture.Usages.FirstOrDefault(value => value.Equals(option.Label, StringComparison.OrdinalIgnoreCase));
        if (option.IsSelected && existing is null) SelectedFurniture.Usages.Add(option.Label);
        else if (!option.IsSelected && existing is not null) SelectedFurniture.Usages.Remove(existing);
        SelectedFurniture.UsageSpecifique = string.Join(", ", SelectedFurniture.Usages);
        InvalidatePublicationReview();
    }

    public async Task ReloadTaxonomyAsync()
    {
        _taxonomy = await ComponentTaxonomyStore.LoadAsync(SharedRoot);
        RebuildFurnitureTagOptions();
        RebuildClientCards();
        OnPropertyChanged(nameof(InheritedTags));
        InvalidatePublicationReview();
        TaxonomyChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> SaveCatalogAsync() => SaveAsync();

    private void RebuildFurnitureTagOptions()
    {
        FurnitureTagOptions.Clear();
        if (SelectedFurniture is null) return;
        SelectedFurniture.AddedTagIds ??= [];
        SelectedFurniture.RemovedInheritedTagIds ??= [];
        var inheritedIds = LinkedComponents.SelectMany(component => ComponentTaxonomyStore.Resolve(component, _taxonomy)).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in _taxonomy.Tags.Where(x => x.IsActive).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var selected = (inheritedIds.Contains(tag.Id) && !SelectedFurniture.RemovedInheritedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase)) || SelectedFurniture.AddedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
            FurnitureTagOptions.Add(new ToggleOptionViewModel(tag.Label, selected, option => ToggleFurnitureTag(tag, inheritedIds.Contains(tag.Id), option.IsSelected)));
        }
    }

    private void ToggleFurnitureTag(ComponentTagRecord tag, bool inherited, bool selected)
    {
        if (SelectedFurniture is null) return;
        RemoveIgnoreCase(SelectedFurniture.AddedTagIds, tag.Id);
        RemoveIgnoreCase(SelectedFurniture.RemovedInheritedTagIds, tag.Id);
        if (selected && !inherited) SelectedFurniture.AddedTagIds.Add(tag.Id);
        if (!selected && inherited) SelectedFurniture.RemovedInheritedTagIds.Add(tag.Id);
        OnPropertyChanged(nameof(InheritedTags));
        StatusText = "Tags du meuble modifiés. Pensez à enregistrer.";
    }

    private void ToggleUniverse(ToggleOptionViewModel option)
    {
        if (SelectedFurniture is null) return;
        var existing = SelectedFurniture.Universes.FirstOrDefault(value => value.Equals(option.Label, StringComparison.OrdinalIgnoreCase));
        if (option.IsSelected && existing is null) SelectedFurniture.Universes.Add(option.Label); else if (!option.IsSelected && existing is not null) SelectedFurniture.Universes.Remove(existing);
        InvalidatePublicationReview();
        RebuildClientCards();
    }

    private void AddUniverse()
    {
        var name = NewUniverseName.Trim();
        if (_catalog.UniverseDefinitions.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { AtlasDialog.Info("Cet univers existe déjà.", "Univers"); return; }
        _catalog.UniverseDefinitions.Add(new CatalogUniverseRecord { Name = name, SortOrder = _catalog.UniverseDefinitions.Count, Description = DefaultUniverseDescription(name) });
        NormalizeUniverseDefinitions();
        NewUniverseName = ""; RebuildUniverseOptions(); RebuildClientCards(); StatusText = $"Univers « {name} » ajouté au référentiel.";
    }

    public void ReplaceUniverses(IEnumerable<string> universes)
    {
        var names = universes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _catalog.UniverseDefinitions = names.Select((name, index) => _catalog.UniverseDefinitions.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? new CatalogUniverseRecord { Name = name, SortOrder = index, Description = DefaultUniverseDescription(name) }).ToList();
        NormalizeUniverseDefinitions();
        RebuildUniverseOptions();
        RebuildClientCards();
        StatusText = "Référentiel des univers modifié. Pensez à enregistrer le catalogue.";
    }

    public void ReplaceUniverseDefinitions(IEnumerable<CatalogUniverseRecord> universes)
    {
        _catalog.UniverseDefinitions = universes.Where(item => !string.IsNullOrWhiteSpace(item.Name)).ToList();
        NormalizeUniverseDefinitions();
        RebuildUniverseOptions();
        RebuildClientCards();
        OnPropertyChanged(nameof(CatalogUniverses));
        OnPropertyChanged(nameof(CatalogUniverseDefinitions));
        StatusText = "Référentiel illustré des univers modifié. Enregistrez pour le partager.";
    }

    public void RefreshUniversePresentation()
    {
        NormalizeUniverseDefinitions();
        RebuildClientFacets();
        RefreshClientFurnitureView();
        OnPropertyChanged(nameof(CatalogUniverses));
        OnPropertyChanged(nameof(CatalogUniverseDefinitions));
    }

    private void AddComponent()
    {
        if (SelectedFurniture is null || SelectedCompositionCandidate is null) return;
        AddOrIncrementComponent(SelectedCompositionCandidate.Id);
        InvalidatePublicationReview();
        RefreshLinkedComponents();
    }

    private void AddMarkedComponents()
    {
        if (SelectedFurniture is null) return;
        var added = 0;
        foreach (var card in ComponentCards.Where(item => item.IsMarked).ToArray())
        {
            AddOrIncrementComponent(card.Id);
            card.IsMarked = false;
            added++;
        }
        InvalidatePublicationReview();
        RefreshLinkedComponents(); StatusText = $"{added} composant(s) ajouté(s) à la composition. Une référence déjà présente voit sa quantité augmenter.";
    }

    private void RemoveComponent()
    {
        if (SelectedFurniture is null || SelectedLinkedComponent is null) return;
        var line = SelectedFurniture.ComponentLines.FirstOrDefault(x => x.ComponentId == SelectedLinkedComponent.Id);
        if (line is not null) SelectedFurniture.ComponentLines.Remove(line);
        InvalidatePublicationReview();
        RefreshLinkedComponents();
    }

    public void SetSelectedCompositionLines(IEnumerable<FurnitureCompositionLineViewModel> lines)
    {
        _selectedCompositionLines.Clear();
        foreach (var line in lines) _selectedCompositionLines.Add(line);
        OnPropertyChanged(nameof(MarkedCompositionLineCount));
        OnPropertyChanged(nameof(RemoveCompositionLabel));
        RemoveMarkedCompositionCommand.RaiseCanExecuteChanged();
    }

    private void RemoveSelectedComposition()
    {
        if (SelectedFurniture is null) return;
        var selected = _selectedCompositionLines.ToArray();
        if (selected.Length == 0 || !AtlasDialog.Confirm($"Retirer {selected.Length} ligne(s) de la composition ?", "Composition")) return;
        foreach (var line in selected) SelectedFurniture.ComponentLines.Remove(line.Line);
        InvalidatePublicationReview();
        RefreshLinkedComponents();
    }

    private async Task PublishFurnitureAsync()
    {
        if (SelectedFurniture is null) return;
        if (Furniture.Any(value => value.Id != SelectedFurniture.Id && string.Equals(value.Reference, SelectedFurniture.Reference, StringComparison.OrdinalIgnoreCase))) { AtlasDialog.Warning("Cette référence Atlas est déjà utilisée par un autre meuble.", "Publication bloquée"); CurrentFurnitureStep = "Identity"; return; }
        if (string.IsNullOrWhiteSpace(SelectedFurniture.SourceRelativePath)) { AtlasDialog.Warning("Le meuble peut être enregistré en brouillon, mais il faut lui associer un fichier .TOP avant publication.", "Publication bloquée"); CurrentFurnitureStep = "Identity"; return; }
        if (SelectedFurniture.Universes.Count == 0 || string.IsNullOrWhiteSpace(SelectedFurniture.TypeMeuble)) { AtlasDialog.Warning("Le meuble peut être enregistré en brouillon, mais un univers et un type sont obligatoires avant publication.", "Publication bloquée"); CurrentFurnitureStep = "Classification"; return; }
        if (SelectedFurniture.ComponentLines.Count == 0) { AtlasDialog.Warning("Le meuble peut être enregistré en brouillon, mais sa composition ne peut pas être vide avant publication.", "Publication bloquée"); CurrentFurnitureStep = "Composition"; return; }
        if (!SelectedFurniture.IsPublicationReviewed) { AtlasDialog.Warning("Enregistrement possible, mais publication impossible : cochez la validation finale après avoir contrôlé le récapitulatif.", "Publication bloquée"); CurrentFurnitureStep = "Review"; return; }
        if (!AtlasDialog.Confirm($"Publier « {SelectedFurniture.DisplayName} » dans le Catalogue Atlas ?", "Publication", "La fiche sera enregistrée dans l’espace partagé et apparaîtra immédiatement dans l’aperçu client.")) return;
        var publishedId = SelectedFurniture.Id;
        var previousStatus = SelectedFurniture.Status;
        var previousValidator = SelectedFurniture.ValidatedBy;
        var previousValidationDate = SelectedFurniture.ValidatedUtc;
        SelectedFurniture.Status = RecordStatus.Publiee; SelectedFurniture.ValidatedBy = CurrentUser.DisplayName; SelectedFurniture.ValidatedUtc = DateTimeOffset.UtcNow;
        if (!await SaveAsync())
        {
            SelectedFurniture.Status = previousStatus;
            SelectedFurniture.ValidatedBy = previousValidator;
            SelectedFurniture.ValidatedUtc = previousValidationDate;
            return;
        }
        RebuildClientCards();
        SelectedClientFurnitureCard = ClientFurnitureCards.FirstOrDefault(x => x.Record.Id == publishedId);
        CurrentPage = "Catalog";
        NotifySummary(); StatusText = "Meuble publié et enregistré dans le Catalogue Atlas.";
        AtlasDialog.Info("Le meuble est publié et visible dans le Catalogue Atlas.", "Publication terminée");
    }

    private void RefreshLinkedComponents()
    {
        LinkedComponents.Clear(); CompositionLines.Clear(); _selectedCompositionLines.Clear();
        if (SelectedFurniture is not null)
        {
            SelectedFurniture.ComponentLines ??= [];
            foreach (var line in SelectedFurniture.ComponentLines)
            {
                var component = Components.FirstOrDefault(item => item.Id == line.ComponentId);
                var card = ComponentCards.FirstOrDefault(item => item.Id == line.ComponentId);
                if (component is null || card is null) continue;
                if (!LinkedComponents.Contains(component)) LinkedComponents.Add(component);
                var row = new FurnitureCompositionLineViewModel(line, card);
                row.PropertyChanged += CompositionLineOnPropertyChanged;
                CompositionLines.Add(row);
            }
            SyncLegacyComponentIds(SelectedFurniture);
        }
        RebuildFurnitureTagOptions(); OnPropertyChanged(nameof(InheritedTags)); OnPropertyChanged(nameof(InheritedTagsByCategory)); OnPropertyChanged(nameof(InheritedFamilies)); OnPropertyChanged(nameof(InheritedCompatibility)); OnPropertyChanged(nameof(CompositionTotalQuantity)); OnPropertyChanged(nameof(CharacteristicWarnings));
        OnPropertyChanged(nameof(MarkedCompositionLineCount)); OnPropertyChanged(nameof(RemoveCompositionLabel)); RemoveMarkedCompositionCommand.RaiseCanExecuteChanged();
    }

    public string CharacteristicWarnings
    {
        get
        {
            var warnings = new List<string>();
            foreach (var component in LinkedComponents)
            {
                var family = component.EffectiveFamilyName.ToLowerInvariant();
                var tags = ComponentTaxonomyStore.Resolve(component, _taxonomy);
                if ((family.Contains("couliss") || family.Contains("tiroir")) && !tags.Any(x => x.Category is "Marque" or "Gamme")) warnings.Add($"{component.DisplayName} : marque ou gamme de coulissant à compléter");
                if ((family.Contains("charni") || family.Contains("façade") || family.Contains("facade")) && !tags.Any(x => x.Category is "Marque" or "Implantation")) warnings.Add($"{component.DisplayName} : quincaillerie ou implantation de façade à compléter");
                if (family.Contains("pied") && !tags.Any(x => x.Category is "Marque" or "Gamme")) warnings.Add($"{component.DisplayName} : système de pied à compléter");
            }
            return warnings.Count == 0 ? "Aucun avertissement technique détecté." : "À compléter (sans bloquer le MVP) :\n" + string.Join("\n", warnings.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    private void SelectedFurnitureOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FurnitureRecord.SourceRelativePath) or nameof(FurnitureRecord.ImageRelativePath))
        {
            LoadFurniturePreview();
            OpenFurnitureFolderCommand.RaiseCanExecuteChanged();
        }
        if (e.PropertyName is nameof(FurnitureRecord.DisplayName) or nameof(FurnitureRecord.Reference) or nameof(FurnitureRecord.Status)) RefreshFurnitureView();
        if (e.PropertyName != nameof(FurnitureRecord.IsPublicationReviewed)) InvalidatePublicationReview();
    }

    private int FurnitureStepIndex => Math.Max(0, FurnitureSteps.ToList().FindIndex(x => x.Key.Equals(CurrentFurnitureStep, StringComparison.OrdinalIgnoreCase)));

    private void MoveFurnitureStep(int direction)
    {
        var index = Math.Clamp(FurnitureStepIndex + direction, 0, FurnitureSteps.Count - 1);
        CurrentFurnitureStep = FurnitureSteps[index].Key;
    }

    private void AddOrIncrementComponent(string componentId)
    {
        if (SelectedFurniture is null) return;
        SelectedFurniture.ComponentLines ??= [];
        var existing = SelectedFurniture.ComponentLines.FirstOrDefault(x => x.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase));
        if (existing is null) SelectedFurniture.ComponentLines.Add(new FurnitureComponentLine { ComponentId = componentId, Quantity = 1 });
        else existing.Quantity++;
        SyncLegacyComponentIds(SelectedFurniture);
    }

    private void CompositionLineOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FurnitureCompositionLineViewModel.IsMarked)) RemoveMarkedCompositionCommand.RaiseCanExecuteChanged();
        if (e.PropertyName == nameof(FurnitureCompositionLineViewModel.Quantity))
        {
            InvalidatePublicationReview();
            OnPropertyChanged(nameof(CompositionTotalQuantity));
        }
    }

    private void InvalidatePublicationReview()
    {
        if (SelectedFurniture is not null && SelectedFurniture.IsPublicationReviewed) SelectedFurniture.IsPublicationReviewed = false;
    }

    private static void SyncLegacyComponentIds(FurnitureRecord furniture) =>
        furniture.ComponentIds = furniture.ComponentLines.Select(x => x.ComponentId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private string NavBackground(string page) => CurrentPage.Equals(page, StringComparison.OrdinalIgnoreCase) ? "#214E86" : "Transparent";

    private string ResolveLibraryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return Path.IsPathRooted(path) ? path : Path.Combine(Settings.LibraryRoot, path);
    }

    private void LoadFurniturePreview()
    {
        FurniturePreview = null;
        if (SelectedFurniture is null) return;
        var imagePath = ResolveLibraryPath(SelectedFurniture.ImageRelativePath);
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return;
        try
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 620;
            image.UriSource = new Uri(imagePath, UriKind.Absolute); image.EndInit(); image.Freeze(); FurniturePreview = image;
        }
        catch { FurniturePreview = null; }
    }

    private void ChooseFurnitureTop()
    {
        if (SelectedFurniture is null) return;
        var dialog = new OpenFileDialog { Title = $"Choisir le fichier TopSolid à enregistrer sous {SelectedFurniture.Reference}.top", Filter = "Fichiers TopSolid (*.top)|*.top|Tous les fichiers (*.*)|*.*", CheckFileExists = true };
        if (Directory.Exists(Settings.LibraryRoot)) dialog.InitialDirectory = Settings.LibraryRoot;
        if (dialog.ShowDialog() != true) return;
        try
        {
            var sourceTop = dialog.FileName;
            var targetTop = Path.Combine(Path.GetDirectoryName(sourceTop)!, $"{SelectedFurniture.Reference}.top");
            if (!Path.GetFullPath(sourceTop).Equals(Path.GetFullPath(targetTop), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(targetTop)) throw new IOException($"Le fichier {SelectedFurniture.Reference}.top existe déjà dans ce dossier.");
                File.Copy(sourceTop, targetTop, false);
            }
            SelectedFurniture.SourceRelativePath = MakeLibraryRelative(targetTop);
            var sourceImage = new[] { sourceTop + ".png", Path.ChangeExtension(sourceTop, ".png") }.FirstOrDefault(File.Exists);
            string? targetImage = null;
            if (sourceImage is not null)
            {
                targetImage = targetTop + ".png";
                if (!Path.GetFullPath(sourceImage).Equals(Path.GetFullPath(targetImage), StringComparison.OrdinalIgnoreCase)) File.Copy(sourceImage, targetImage, false);
            }
            SelectedFurniture.ImageRelativePath = targetImage is null ? string.Empty : MakeLibraryRelative(targetImage);
            StatusText = targetImage is null ? $"Copie créée : {SelectedFurniture.Reference}.top. Aucun aperçu trouvé." : $"Meuble et aperçu enregistrés sous la référence {SelectedFurniture.Reference}.";
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Impossible d'enregistrer le meuble", "Le fichier source n'a pas été modifié.");
        }
    }

    private string MakeLibraryRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(Settings.LibraryRoot)) return path;
        try
        {
            var relative = Path.GetRelativePath(Settings.LibraryRoot, path);
            return relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
        }
        catch { return path; }
    }

    private void OpenFurnitureFolder()
    {
        if (SelectedFurniture is null) return;
        try
        {
            var file = ResolveLibraryPath(SelectedFurniture.SourceRelativePath);
            var folder = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { AtlasDialog.Warning("Le dossier du meuble est introuvable.", "Emplacement du meuble"); return; }
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private string JoinInherited(Func<ComponentRecord, string> selector) => string.Join(", ", LinkedComponents.SelectMany(item => selector(item).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

    private bool FilterComponent(object item)
    {
        if (item is not ComponentCardViewModel card) return false;
        if (SelectedLibraryFilter is { Name: not "Toutes les bibliothèques" } && NormalizeBucket(card.Library) != SelectedLibraryFilter.Name) return false;
        if (ActiveFamilyFilter != "Toutes" && NormalizeBucket(card.Family) != ActiveFamilyFilter) return false;
        if (SelectedComponentType != "Tous les types" && card.Type != SelectedComponentType) return false;
        if (string.IsNullOrWhiteSpace(ComponentSearch)) return true;
        var query = ComponentSearch.Trim(); return new[] { card.Name, card.TechnicalName, card.Library, card.Family, card.Type }.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterFurniture(object item)
    {
        if (item is not FurnitureRecord furniture) return false;
        var expectedStatus = SelectedFurnitureStatus switch
        {
            "Brouillon" => RecordStatus.Brouillon,
            "À contrôler" => RecordStatus.AControler,
            "Validée" => RecordStatus.Validee,
            "Retenue" => RecordStatus.Retenue,
            "Publiée" => RecordStatus.Publiee,
            _ => (RecordStatus?)null
        };
        if (expectedStatus is not null && furniture.Status != expectedStatus) return false;
        if (string.IsNullOrWhiteSpace(FurnitureSearch)) return true;
        var query = FurnitureSearch.Trim(); return new[] { furniture.Reference, furniture.DisplayName, furniture.Family, furniture.Description, furniture.TypeMeuble }.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterClientFurniture(object item)
    {
        if (item is not FurnitureCardViewModel card || card.Record.Status != RecordStatus.Publiee) return false;
        var resolvedTags = ComponentTaxonomyStore.Resolve(card.Record, Components, _taxonomy);
        if (!MatchesClientFacet(ClientUniverseFacets, card.Record.Universes)) return false;
        if (!MatchesClientFacet(ClientTypeFacets, [card.Record.TypeMeuble])) return false;
        if (!MatchesClientFacet(ClientUsageFacets, FurnitureUsagesFor(card.Record))) return false;
        if (!MatchesClientFacet(ClientFamilyFacets, [card.Record.Family])) return false;
        if (!MatchesClientFacet(ClientTagFacets, resolvedTags.Select(tag => tag.Label))) return false;
        if (!MatchesClientFacet(ClientFormFacets, [card.Record.Forme])) return false;
        if (!MatchesClientFacet(ClientConstructionFacets, [card.Record.PrincipleConstruction])) return false;
        if (!MatchesClientFacet(ClientAssemblyFacets, [card.Record.TypeAssemblage])) return false;
        if (!MatchesClientFacet(ClientBackFacets, [card.Record.PositionDos])) return false;
        if (string.IsNullOrWhiteSpace(ClientSearch)) return true;
        return card.SearchScore >= 0.58d;
    }

    private static bool MatchesClientFacet(IEnumerable<CatalogFacetViewModel> facets, IEnumerable<string> values)
    {
        var selected = facets.Where(option => option.IsSelected).Select(option => option.Label).ToArray();
        if (selected.Length == 0) return true;
        var candidates = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selected.Any(candidates.Contains);
    }

    private static string NormalizeBucket(string value) => string.IsNullOrWhiteSpace(value) ? "Non classés" : value;

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }

    private void ChooseLibrary()
    {
        var dialog = new OpenFolderDialog { Title = "Choisir la bibliothèque TopSolid", Multiselect = false }; if (Directory.Exists(Settings.LibraryRoot)) dialog.InitialDirectory = Settings.LibraryRoot;
        if (dialog.ShowDialog() == true) { Settings.LibraryRoot = dialog.FolderName; RebuildComponentCards(); RebuildClientCards(); }
    }

    private void ChooseSharedRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choisir le dossier partagé Atlas", Multiselect = false }; if (Directory.Exists(SharedRoot)) dialog.InitialDirectory = SharedRoot;
        if (dialog.ShowDialog() == true) SharedRoot = dialog.FolderName;
    }

    private async Task SaveBootstrapAsync() { _bootstrap.SharedRoot = SharedRoot; await SharedCatalogStore.SaveBootstrapAsync(_bootstrap); AtlasDialog.Info("Le nouvel emplacement sera utilisé au prochain lancement.", "Dossier partagé"); }
    private void OpenSharedRoot() { try { Process.Start(new ProcessStartInfo("explorer.exe", _store.SharedRoot) { UseShellExecute = true }); } catch (Exception exception) { ShowError(exception); } }

    private async Task CheckUpdateAsync(bool silent)
    {
        IsBusy = true; UpdateLabel = "Vérification…";
        try
        {
            var available = await _updater.CheckAsync();
            if (available is null) { UpdateLabel = "Application à jour"; if (!silent) AtlasDialog.Info("Vous utilisez la dernière version.", "Mise à jour"); return; }
            UpdateLabel = $"Installer {available}";
            if (!AtlasDialog.Confirm($"La version {available} est disponible. La télécharger et redémarrer Atlas ?", "Mise à jour automatique")) return;
            UpdateLabel = "Téléchargement…";
            var automatic = await _updater.DownloadAndInstallAsync(progress => UpdateLabel = $"Téléchargement {progress}%");
            if (automatic)
            {
                StatusText = "Mise à jour téléchargée. Atlas va redémarrer.";
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                UpdateLabel = $"Télécharger {available}";
                AtlasDialog.Info("Cette installation ne contient pas encore le nouveau module de mise à jour. Le téléchargement va s’ouvrir : extrayez une dernière fois le pack complet. Les versions suivantes s’installeront automatiquement.", "Une dernière installation manuelle");
                _updater.OpenDownloadPage();
            }
        }
        catch (Exception exception) { UpdateLabel = "Mise à jour indisponible"; if (!silent) ShowError(exception); }
        finally { IsBusy = false; }
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(EnvironmentLabel)); OnPropertyChanged(nameof(ComponentCount)); OnPropertyChanged(nameof(FurnitureCount)); OnPropertyChanged(nameof(PublishedCount)); OnPropertyChanged(nameof(HealthIssueCount)); OnPropertyChanged(nameof(LastModification)); RefreshClientFurnitureView();
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { SaveCommand, ReloadCommand, ScanCommand, ValidateComponentCommand, AddComponentCommand, AddMarkedComponentsCommand, RemoveComponentCommand, RemoveMarkedCompositionCommand, PublishFurnitureCommand, PreviousFurnitureStepCommand, NextFurnitureStepCommand, CheckUpdateCommand, ChooseFurnitureTopCommand, OpenFurnitureFolderCommand, PrepareTopSolidBridgeCommand }) command.RaiseCanExecuteChanged();
    }

    private sealed class ClientFurnitureSearchComparer(Func<string> query) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not FurnitureCardViewModel left || y is not FurnitureCardViewModel right) return 0;
            if (!string.IsNullOrWhiteSpace(query()))
            {
                var score = right.SearchScore.CompareTo(left.SearchScore);
                if (score != 0) return score;
            }
            return StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        }
    }

    private static void ShowError(Exception exception) => AtlasDialog.Error(exception.Message, "Biblidéo Atlas");
}
