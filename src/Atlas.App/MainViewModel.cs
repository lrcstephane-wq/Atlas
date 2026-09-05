using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
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
    private static readonly string[] DefaultUniverses = ["Cuisine", "Dressing", "Salle de bain", "Bibliothèque", "Séjour", "Bureau / Tertiaire", "Buanderie", "Agencement commercial", "Chambre", "Hôtellerie / Hébergement", "Restaurant / Bar"];
    private readonly SharedCatalogStore _store;
    private readonly UserAccountStore _userStore;
    private readonly LocalBootstrap _bootstrap;
    private readonly LibraryScanner _scanner = new();
    private readonly ApplicationUpdateService _updater = new();
    private AtlasCatalog _catalog = new();
    private string _currentPage = "Dashboard", _componentSearch = "", _furnitureSearch = "", _clientSearch = "", _statusText = "Initialisation…", _updateLabel = "Rechercher une mise à jour";
    private string _sharedRoot, _activeFamilyFilter = "Toutes", _selectedComponentType = "Tous les types", _selectedClientUniverse = "Tous les univers", _selectedClientType = "Tous les types";
    private string _currentFurnitureStep = "Identity", _creationMode = "Quick", _newUniverseName = "";
    private ComponentCardViewModel? _selectedComponentCard;
    private FurnitureRecord? _selectedFurniture;
    private ComponentRecord? _selectedCompositionCandidate, _selectedLinkedComponent;
    private FurnitureCardViewModel? _selectedClientFurnitureCard;
    private FurnitureFamilyRecord? _selectedFurnitureFamily;
    private LibraryFilterViewModel? _selectedLibraryFilter;
    private bool _isNavigationExpanded = true, _isComponentMosaic = true, _isBusy;

    public MainViewModel(SharedCatalogStore store, UserAccountStore userStore, LocalBootstrap bootstrap, UserAccount currentUser)
    {
        _store = store; _userStore = userStore; _bootstrap = bootstrap; _sharedRoot = bootstrap.SharedRoot; CurrentUser = currentUser;
        ComponentView = CollectionViewSource.GetDefaultView(ComponentCards); ComponentView.Filter = FilterComponent;
        FurnitureView = CollectionViewSource.GetDefaultView(Furniture); FurnitureView.Filter = FilterFurniture;
        ClientFurnitureView = CollectionViewSource.GetDefaultView(ClientFurnitureCards); ClientFurnitureView.Filter = FilterClientFurniture;

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
        CreateFurnitureCommand = new(_ => CreateFurniture(), _ => CanEdit);
        CreateFamilyCommand = new(_ => CreateFamily(), _ => CanEdit);
        SetCreationModeCommand = new(value => CreationMode = value?.ToString() ?? "Quick");
        NavigateFurnitureStepCommand = new(value => CurrentFurnitureStep = value?.ToString() ?? "Identity");
        AddUniverseCommand = new(_ => AddUniverse(), _ => CanEdit && !string.IsNullOrWhiteSpace(NewUniverseName));
        AddComponentCommand = new(_ => AddComponent(), _ => CanEdit && SelectedFurniture is not null && SelectedCompositionCandidate is not null);
        AddMarkedComponentsCommand = new(_ => AddMarkedComponents(), _ => CanEdit && SelectedFurniture is not null && MarkedComponentCount > 0);
        RemoveComponentCommand = new(_ => RemoveComponent(), _ => CanEdit && SelectedFurniture is not null && SelectedLinkedComponent is not null);
        PublishFurnitureCommand = new(_ => PublishFurniture(), _ => CanValidate && SelectedFurniture is not null);
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
    public ObservableCollection<UserAccount> Users { get; } = [];
    public ObservableCollection<LibraryFilterViewModel> LibraryFilters { get; } = [];
    public ObservableCollection<FilterOptionViewModel> FamilyFilters { get; } = [];
    public ObservableCollection<ToggleOptionViewModel> UniverseOptions { get; } = [];
    public ObservableCollection<string> ClientUniverseOptions { get; } = ["Tous les univers"];
    public ObservableCollection<string> ComponentTypeOptions { get; } = ["Tous les types"];
    public ObservableCollection<string> ClientTypeOptions { get; } = ["Tous les types"];
    public ICollectionView ComponentView { get; }
    public ICollectionView FurnitureView { get; }
    public ICollectionView ClientFurnitureView { get; }
    public IReadOnlyList<CatalogEnvironment> Environments { get; } = Enum.GetValues<CatalogEnvironment>();
    public IReadOnlyList<RecordStatus> Statuses { get; } = Enum.GetValues<RecordStatus>();
    public IReadOnlyList<string> RoleProfiles { get; } = ["Lecture seule", "Éditeur", "Éditeur + validateur", "Administrateur"];
    public IReadOnlyList<string> FurnitureTypes { get; } = ["Meuble bas", "Meuble haut", "Colonne", "Demi-colonne", "Niche", "Armoire", "Étagère", "Banc", "Bureau", "Console", "Comptoir", "Présentoir"];
    public IReadOnlyList<string> FurnitureUsages { get; } = ["Sous-évier", "Vasque", "Four", "Micro-ondes", "Réfrigérateur", "Lave-vaisselle", "Lave-linge", "Sèche-linge", "Poubelle", "Penderie", "Chaussures", "TV / multimédia", "Imprimante", "Caisse", "Présentation / exposition", "Technique"];
    public IReadOnlyList<string> FurnitureForms { get; } = ["Droit", "Angle", "Courbe", "Trapèze", "Pan coupé", "Sous rampant"];
    public IReadOnlyList<string> ConstructionPrinciples { get; } = ["Montant filant", "Traverse filante"];
    public IReadOnlyList<string> BackPositions { get; } = ["Sans dos", "Appliqué", "Rainuré", "Intérieur"];
    public IReadOnlyList<string> AssemblyTypes { get; } = ["Vis", "Tourillons", "Tourillons + vis", "Excentrique", "Tourillons + excentriques", "Clamex", "Cabineo", "Vis auto-tourillonnante"];
    public IReadOnlyList<string> DoorTypes { get; } = ["Applique", "Semi-applique", "Encastrée"];
    public IReadOnlyList<string> DrawerTypes { get; } = ["Applique", "Encastré"];

    public WorkspaceSettings Settings => _catalog.Settings;
    public string Version => _updater.CurrentVersion;
    public string CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }
    public string SharedRoot { get => _sharedRoot; set => SetProperty(ref _sharedRoot, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string UpdateLabel { get => _updateLabel; set => SetProperty(ref _updateLabel, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); } }
    public bool IsNavigationExpanded { get => _isNavigationExpanded; set { if (SetProperty(ref _isNavigationExpanded, value)) OnPropertyChanged(nameof(NavigationWidth)); } }
    public double NavigationWidth => IsNavigationExpanded ? 268 : 82;
    public bool IsComponentMosaic { get => _isComponentMosaic; set => SetProperty(ref _isComponentMosaic, value); }
    public bool IsFamilyMode => CreationMode == "Family";
    public string EnvironmentLabel => Settings.Environment == CatalogEnvironment.NonConfigure ? "ENV. À CONFIGURER" : $"ENV. {Settings.Environment}";
    public int ComponentCount => Components.Count(item => !item.IsDemo);
    public int FurnitureCount => Furniture.Count(item => !item.IsDemo);
    public int PublishedCount => Furniture.Count(item => item.Status == RecordStatus.Publiee);
    public int HealthIssueCount => Components.Count(item => !item.IsNameCompliant || item.IsMissing) + Furniture.Count(item => item.ComponentIds.Count == 0);
    public int MarkedComponentCount => ComponentCards.Count(item => item.IsMarked);
    public string VisibleComponentLabel => $"{ComponentView.Cast<object>().Count():N0} affichés sur {ComponentCards.Count:N0}";
    public string LastModification => _catalog.Revision == 0 ? "Espace de découverte" : $"Révision {_catalog.Revision} · {_catalog.ModifiedBy}";

    public string ComponentSearch { get => _componentSearch; set { if (SetProperty(ref _componentSearch, value)) RefreshComponentView(); } }
    public string FurnitureSearch { get => _furnitureSearch; set { if (SetProperty(ref _furnitureSearch, value)) FurnitureView.Refresh(); } }
    public string ClientSearch { get => _clientSearch; set { if (SetProperty(ref _clientSearch, value)) ClientFurnitureView.Refresh(); } }
    public string ActiveFamilyFilter { get => _activeFamilyFilter; set { if (SetProperty(ref _activeFamilyFilter, value)) { UpdateFamilyFilterStates(); RefreshComponentView(); } } }
    public string SelectedComponentType { get => _selectedComponentType; set { if (SetProperty(ref _selectedComponentType, value)) RefreshComponentView(); } }
    public string SelectedClientUniverse { get => _selectedClientUniverse; set { if (SetProperty(ref _selectedClientUniverse, value)) ClientFurnitureView.Refresh(); } }
    public string SelectedClientType { get => _selectedClientType; set { if (SetProperty(ref _selectedClientType, value)) ClientFurnitureView.Refresh(); } }
    public string CurrentFurnitureStep { get => _currentFurnitureStep; set => SetProperty(ref _currentFurnitureStep, value); }
    public string CreationMode { get => _creationMode; set { if (SetProperty(ref _creationMode, value)) OnPropertyChanged(nameof(IsFamilyMode)); } }
    public string NewUniverseName { get => _newUniverseName; set { if (SetProperty(ref _newUniverseName, value)) AddUniverseCommand.RaiseCanExecuteChanged(); } }
    public LibraryFilterViewModel? SelectedLibraryFilter { get => _selectedLibraryFilter; set { if (SetProperty(ref _selectedLibraryFilter, value)) RefreshComponentView(); } }
    public FurnitureFamilyRecord? SelectedFurnitureFamily { get => _selectedFurnitureFamily; set => SetProperty(ref _selectedFurnitureFamily, value); }
    public ComponentCardViewModel? SelectedComponentCard { get => _selectedComponentCard; set { if (SetProperty(ref _selectedComponentCard, value)) { OnPropertyChanged(nameof(SelectedComponent)); RaiseCommandStates(); } } }
    public ComponentRecord? SelectedComponent => SelectedComponentCard?.Record;
    public FurnitureRecord? SelectedFurniture { get => _selectedFurniture; set { if (SetProperty(ref _selectedFurniture, value)) { RefreshLinkedComponents(); RebuildUniverseOptions(); RaiseCommandStates(); } } }
    public ComponentRecord? SelectedCompositionCandidate { get => _selectedCompositionCandidate; set { if (SetProperty(ref _selectedCompositionCandidate, value)) RaiseCommandStates(); } }
    public ComponentRecord? SelectedLinkedComponent { get => _selectedLinkedComponent; set { if (SetProperty(ref _selectedLinkedComponent, value)) RaiseCommandStates(); } }
    public FurnitureCardViewModel? SelectedClientFurnitureCard { get => _selectedClientFurnitureCard; set { if (SetProperty(ref _selectedClientFurnitureCard, value)) OnPropertyChanged(nameof(SelectedClientFurniture)); } }
    public FurnitureRecord? SelectedClientFurniture => SelectedClientFurnitureCard?.Record;
    public string InheritedCapabilities => JoinInherited(item => item.CapabilitiesCsv);
    public string InheritedCompatibility => JoinInherited(item => item.CompatibilityCsv);

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
    public RelayCommand CreateFurnitureCommand { get; }
    public RelayCommand CreateFamilyCommand { get; }
    public RelayCommand SetCreationModeCommand { get; }
    public RelayCommand NavigateFurnitureStepCommand { get; }
    public RelayCommand AddUniverseCommand { get; }
    public RelayCommand AddComponentCommand { get; }
    public RelayCommand AddMarkedComponentsCommand { get; }
    public RelayCommand RemoveComponentCommand { get; }
    public RelayCommand PublishFurnitureCommand { get; }
    public RelayCommand OpenSharedRootCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }

    public async Task InitializeAsync()
    {
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
            _catalog = await _store.LoadAsync(); NormalizeCatalog();
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
        _catalog.Settings ??= new(); _catalog.Components ??= []; _catalog.Furniture ??= []; _catalog.FurnitureFamilies ??= []; _catalog.Universes ??= [];
        if (_catalog.Universes.Count == 0) _catalog.Universes.AddRange(DefaultUniverses);
        foreach (var item in _catalog.Furniture) { item.Universes ??= []; item.ComponentIds ??= []; }
        _catalog.SchemaVersion = Math.Max(_catalog.SchemaVersion, 2);
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            _catalog.Components = Components.ToList(); _catalog.Furniture = Furniture.ToList(); _catalog.FurnitureFamilies = FurnitureFamilies.ToList();
            await _store.SaveAsync(_catalog, _catalog.Revision, CurrentUser.DisplayName); StatusText = $"Enregistré · révision {_catalog.Revision}"; NotifySummary();
        }
        catch (CatalogConcurrencyException exception) { AtlasDialog.Warning(exception.Message, "Modification concurrente"); }
        catch (Exception exception) { ShowError(exception); }
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
                if (record is null) { record = new() { Id = item.StableId, DisplayName = item.TechnicalName, Status = RecordStatus.Brouillon }; Components.Add(record); }
                record.SourceRelativePath = item.RelativeTopPath; record.PreviewRelativePath = item.PreviewRelativePath; record.LibraryName = item.Library; record.FamilyName = item.Family; record.TechnicalName = item.TechnicalName;
                record.TypeCode = item.Parsed.Type; record.VariantCode = item.Parsed.Variant; record.IndexCode = item.Parsed.Index; record.RangeCode = item.Parsed.Range; record.ConstructionCode = item.Parsed.Construction;
                record.IsNameCompliant = item.IsCompliant; record.IsMissing = false; record.LastSeenUtc = DateTimeOffset.UtcNow;
            }
            RebuildComponentCards(); RebuildClientCards(); NotifySummary(); StatusText = $"{scan.Components.Count:N0} composants indexés · {scan.Warnings.Count} avertissement(s).";
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
        FamilyFilters.Clear(); FamilyFilters.Add(new() { Label = "Toutes", Count = ComponentCards.Count, IsActive = ActiveFamilyFilter == "Toutes" });
        foreach (var group in ComponentCards.GroupBy(item => NormalizeBucket(item.Family)).OrderByDescending(item => item.Count()).ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)) FamilyFilters.Add(new() { Label = group.Key, Count = group.Count(), IsActive = ActiveFamilyFilter == group.Key });
        ComponentTypeOptions.Clear(); ComponentTypeOptions.Add("Tous les types");
        foreach (var type in ComponentCards.Select(item => item.Type).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)) ComponentTypeOptions.Add(type);
        if (!ComponentTypeOptions.Contains(SelectedComponentType)) SelectedComponentType = "Tous les types";
    }

    private void RebuildClientCards()
    {
        ClientFurnitureCards.Clear(); foreach (var item in Furniture) ClientFurnitureCards.Add(new(item, Settings.LibraryRoot));
        ClientUniverseOptions.Clear(); ClientUniverseOptions.Add("Tous les univers"); foreach (var item in _catalog.Universes.Order(StringComparer.CurrentCultureIgnoreCase)) ClientUniverseOptions.Add(item);
        ClientTypeOptions.Clear(); ClientTypeOptions.Add("Tous les types"); foreach (var item in Furniture.Select(x => x.TypeMeuble).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)) ClientTypeOptions.Add(item);
        ClientFurnitureView.Refresh();
    }

    private void ComponentCardOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComponentCardViewModel.IsMarked)) return;
        foreach (var filter in LibraryFilters) filter.MarkedCount = filter.Name == "Toutes les bibliothèques" ? ComponentCards.Count(item => item.IsMarked) : ComponentCards.Count(item => item.IsMarked && NormalizeBucket(item.Library) == filter.Name);
        OnPropertyChanged(nameof(LibraryFilters)); OnPropertyChanged(nameof(MarkedComponentCount)); AddMarkedComponentsCommand.RaiseCanExecuteChanged();
    }

    private void RefreshComponentView() { ComponentView.Refresh(); OnPropertyChanged(nameof(VisibleComponentLabel)); }
    private void ClearComponentFilters() { ComponentSearch = ""; ActiveFamilyFilter = "Toutes"; SelectedComponentType = "Tous les types"; SelectedLibraryFilter = LibraryFilters.FirstOrDefault(); }
    private void MarkVisibleComponents(bool marked) { foreach (var card in ComponentView.Cast<ComponentCardViewModel>().ToArray()) card.IsMarked = marked; }
    private void UpdateFamilyFilterStates() { foreach (var filter in FamilyFilters) filter.IsActive = filter.Label == ActiveFamilyFilter; OnPropertyChanged(nameof(FamilyFilters)); }

    private void ValidateComponent()
    {
        if (SelectedComponent is null) return;
        if (!SelectedComponent.IsNameCompliant && string.IsNullOrWhiteSpace(SelectedComponent.ForcedValidationReason)) { AtlasDialog.Warning("Ce nom n’est pas conforme. Renseignez le motif de validation forcée avant de valider.", "Validation"); return; }
        SelectedComponent.Status = RecordStatus.Validee; SelectedComponent.ValidatedBy = CurrentUser.DisplayName; SelectedComponent.ValidatedUtc = DateTimeOffset.UtcNow;
        StatusText = $"Fiche validée par {CurrentUser.DisplayName}. Pensez à enregistrer."; RefreshComponentView();
    }

    private void CreateFurniture()
    {
        var family = CreationMode == "Family" ? SelectedFurnitureFamily : null;
        var item = new FurnitureRecord
        {
            Reference = $"MEU-{Furniture.Count(value => !value.IsDemo) + 1:0000}", DisplayName = family is null ? "Nouveau meuble" : $"{family.Name} · nouvelle variante",
            Family = family?.Name ?? "", FamilyId = family?.Id ?? "", Description = family?.Description ?? "", TypeMeuble = family?.TypeMeuble ?? "", UsageSpecifique = family?.UsageSpecifique ?? "", Forme = family?.Forme ?? "Droit",
            Universes = family?.Universes.ToList() ?? [], Status = RecordStatus.Brouillon
        };
        Furniture.Add(item); SelectedFurniture = item; FurnitureView.Refresh(); RebuildClientCards(); CurrentPage = "Furniture"; CurrentFurnitureStep = "Identity"; NotifySummary();
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
    }

    private void ToggleUniverse(ToggleOptionViewModel option)
    {
        if (SelectedFurniture is null) return;
        var existing = SelectedFurniture.Universes.FirstOrDefault(value => value.Equals(option.Label, StringComparison.OrdinalIgnoreCase));
        if (option.IsSelected && existing is null) SelectedFurniture.Universes.Add(option.Label); else if (!option.IsSelected && existing is not null) SelectedFurniture.Universes.Remove(existing);
        RebuildClientCards();
    }

    private void AddUniverse()
    {
        var name = NewUniverseName.Trim();
        if (_catalog.Universes.Contains(name, StringComparer.OrdinalIgnoreCase)) { AtlasDialog.Info("Cet univers existe déjà.", "Univers"); return; }
        _catalog.Universes.Add(name); NewUniverseName = ""; RebuildUniverseOptions(); RebuildClientCards(); StatusText = $"Univers « {name} » ajouté au référentiel.";
    }

    private void AddComponent()
    {
        if (SelectedFurniture is null || SelectedCompositionCandidate is null || SelectedFurniture.ComponentIds.Contains(SelectedCompositionCandidate.Id)) return;
        SelectedFurniture.ComponentIds.Add(SelectedCompositionCandidate.Id); RefreshLinkedComponents();
    }

    private void AddMarkedComponents()
    {
        if (SelectedFurniture is null) return; var added = 0;
        foreach (var card in ComponentCards.Where(item => item.IsMarked)) if (!SelectedFurniture.ComponentIds.Contains(card.Id)) { SelectedFurniture.ComponentIds.Add(card.Id); added++; }
        RefreshLinkedComponents(); StatusText = $"{added} composant(s) ajouté(s) à la composition.";
    }

    private void RemoveComponent() { if (SelectedFurniture is null || SelectedLinkedComponent is null) return; SelectedFurniture.ComponentIds.Remove(SelectedLinkedComponent.Id); RefreshLinkedComponents(); }

    private void PublishFurniture()
    {
        if (SelectedFurniture is null) return;
        var invalid = LinkedComponents.Where(item => item.Status is not (RecordStatus.Validee or RecordStatus.Retenue or RecordStatus.Publiee)).ToArray();
        if ((LinkedComponents.Count == 0 || invalid.Length > 0) && string.IsNullOrWhiteSpace(SelectedFurniture.ForcedValidationReason)) { AtlasDialog.Warning("La composition est vide ou contient des composants non validés. Renseignez le motif de forçage pour conserver le dernier mot.", "Publication"); return; }
        if (SelectedFurniture.Universes.Count == 0) { AtlasDialog.Warning("Sélectionnez au moins un univers avant de publier ce meuble.", "Publication"); CurrentFurnitureStep = "Classification"; return; }
        SelectedFurniture.Status = RecordStatus.Publiee; SelectedFurniture.ValidatedBy = CurrentUser.DisplayName; SelectedFurniture.ValidatedUtc = DateTimeOffset.UtcNow;
        RebuildClientCards(); NotifySummary(); StatusText = "Meuble publié dans le Catalogue Atlas. Pensez à enregistrer.";
    }

    private void RefreshLinkedComponents()
    {
        LinkedComponents.Clear();
        if (SelectedFurniture is not null) foreach (var id in SelectedFurniture.ComponentIds) if (Components.FirstOrDefault(item => item.Id == id) is { } component) LinkedComponents.Add(component);
        OnPropertyChanged(nameof(InheritedCapabilities)); OnPropertyChanged(nameof(InheritedCompatibility));
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
        if (item is not FurnitureRecord furniture || string.IsNullOrWhiteSpace(FurnitureSearch)) return true;
        var query = FurnitureSearch.Trim(); return new[] { furniture.Reference, furniture.DisplayName, furniture.Family, furniture.Description, furniture.TypeMeuble }.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterClientFurniture(object item)
    {
        if (item is not FurnitureCardViewModel card || card.Record.Status != RecordStatus.Publiee) return false;
        if (SelectedClientUniverse != "Tous les univers" && !card.Record.Universes.Contains(SelectedClientUniverse, StringComparer.OrdinalIgnoreCase)) return false;
        if (SelectedClientType != "Tous les types" && !card.Record.TypeMeuble.Equals(SelectedClientType, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(ClientSearch)) return true;
        var query = ClientSearch.Trim(); return new[] { card.Reference, card.DisplayName, card.Record.Family, card.Description, card.Record.UsageSpecifique, card.Universes }.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeBucket(string value) => string.IsNullOrWhiteSpace(value) ? "Non classés" : value;

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
            UpdateLabel = $"Télécharger {available}";
            if (AtlasDialog.Confirm($"La version {available} est disponible. Ouvrir son téléchargement officiel ?", "Mise à jour"))
                _updater.OpenDownloadPage();
        }
        catch (Exception exception) { UpdateLabel = "Mise à jour indisponible"; if (!silent) ShowError(exception); }
        finally { IsBusy = false; }
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(EnvironmentLabel)); OnPropertyChanged(nameof(ComponentCount)); OnPropertyChanged(nameof(FurnitureCount)); OnPropertyChanged(nameof(PublishedCount)); OnPropertyChanged(nameof(HealthIssueCount)); OnPropertyChanged(nameof(LastModification)); ClientFurnitureView.Refresh();
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { SaveCommand, ReloadCommand, ScanCommand, ValidateComponentCommand, AddComponentCommand, AddMarkedComponentsCommand, RemoveComponentCommand, PublishFurnitureCommand, CheckUpdateCommand }) command.RaiseCanExecuteChanged();
    }

    private static void ShowError(Exception exception) => AtlasDialog.Error(exception.Message, "Biblidéo Atlas");
}
