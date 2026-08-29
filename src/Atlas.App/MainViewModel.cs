using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Atlas.App.Infrastructure;
using Atlas.App.Services;
using Atlas.Core.Models;
using Atlas.Core.Services;
using Microsoft.Win32;

namespace Atlas.App;

public sealed class MainViewModel : ObservableObject
{
    private readonly SharedCatalogStore _store;
    private readonly UserAccountStore _userStore;
    private readonly LocalBootstrap _bootstrap;
    private readonly LibraryScanner _scanner = new();
    private readonly ApplicationUpdateService _updater = new();
    private AtlasCatalog _catalog = new();
    private string _currentPage = "Dashboard";
    private string _componentSearch = string.Empty;
    private string _furnitureSearch = string.Empty;
    private string _clientSearch = string.Empty;
    private string _statusText = "Initialisation…";
    private string _sharedRoot;
    private ComponentRecord? _selectedComponent;
    private FurnitureRecord? _selectedFurniture;
    private ComponentRecord? _selectedCompositionCandidate;
    private ComponentRecord? _selectedLinkedComponent;
    private FurnitureRecord? _selectedClientFurniture;
    private bool _isBusy;
    private string _updateLabel = "Rechercher une mise à jour";

    public MainViewModel(SharedCatalogStore store, UserAccountStore userStore, LocalBootstrap bootstrap, UserAccount currentUser)
    {
        _store = store;
        _userStore = userStore;
        _bootstrap = bootstrap;
        _sharedRoot = bootstrap.SharedRoot;
        CurrentUser = currentUser;
        ComponentView = CollectionViewSource.GetDefaultView(Components);
        ComponentView.Filter = FilterComponent;
        FurnitureView = CollectionViewSource.GetDefaultView(Furniture);
        FurnitureView.Filter = FilterFurniture;
        ClientFurnitureView = new ListCollectionView(Furniture);
        ClientFurnitureView.Filter = FilterClientFurniture;

        NavigateCommand = new RelayCommand(page => CurrentPage = page?.ToString() ?? "Dashboard");
        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => CanEdit && !IsBusy);
        ReloadCommand = new RelayCommand(_ => _ = ReloadAsync(), _ => !IsBusy);
        ScanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => CanEdit && !IsBusy);
        ChooseLibraryCommand = new RelayCommand(_ => ChooseLibrary(), _ => CanEdit);
        ChooseSharedRootCommand = new RelayCommand(_ => ChooseSharedRoot(), _ => IsAdministrator);
        SaveBootstrapCommand = new RelayCommand(_ => _ = SaveBootstrapAsync(), _ => IsAdministrator);
        ValidateComponentCommand = new RelayCommand(_ => ValidateComponent(), _ => CanValidate && SelectedComponent is not null);
        CreateFurnitureCommand = new RelayCommand(_ => CreateFurniture(), _ => CanEdit);
        AddComponentCommand = new RelayCommand(_ => AddComponent(), _ => CanEdit && SelectedFurniture is not null && SelectedCompositionCandidate is not null);
        RemoveComponentCommand = new RelayCommand(_ => RemoveComponent(), _ => CanEdit && SelectedFurniture is not null && SelectedLinkedComponent is not null);
        PublishFurnitureCommand = new RelayCommand(_ => PublishFurniture(), _ => CanValidate && SelectedFurniture is not null);
        OpenSharedRootCommand = new RelayCommand(_ => OpenSharedRoot());
        CheckUpdateCommand = new RelayCommand(_ => _ = CheckUpdateAsync(false), _ => !IsBusy);
    }

    public UserAccount CurrentUser { get; }
    public bool CanEdit => CurrentUser.CanEdit;
    public bool CanValidate => CurrentUser.CanValidate;
    public bool IsAdministrator => CurrentUser.IsAdministrator;
    public ObservableCollection<ComponentRecord> Components { get; } = [];
    public ObservableCollection<FurnitureRecord> Furniture { get; } = [];
    public ObservableCollection<ComponentRecord> LinkedComponents { get; } = [];
    public ObservableCollection<UserAccount> Users { get; } = [];
    public ICollectionView ComponentView { get; }
    public ICollectionView FurnitureView { get; }
    public ICollectionView ClientFurnitureView { get; }
    public IReadOnlyList<CatalogEnvironment> Environments { get; } = Enum.GetValues<CatalogEnvironment>();
    public IReadOnlyList<RecordStatus> Statuses { get; } = Enum.GetValues<RecordStatus>();
    public IReadOnlyList<string> RoleProfiles { get; } = ["Lecture seule", "Éditeur", "Éditeur + validateur", "Administrateur"];

    public WorkspaceSettings Settings => _catalog.Settings;
    public string Version => _updater.CurrentVersion;
    public string CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }
    public string SharedRoot { get => _sharedRoot; set => SetProperty(ref _sharedRoot, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string UpdateLabel { get => _updateLabel; set => SetProperty(ref _updateLabel, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); } }
    public string EnvironmentLabel => Settings.Environment == CatalogEnvironment.NonConfigure ? "ENV. À CONFIGURER" : $"ENV. {Settings.Environment}";
    public int ComponentCount => Components.Count(item => !item.IsDemo);
    public int FurnitureCount => Furniture.Count(item => !item.IsDemo);
    public int PublishedCount => Furniture.Count(item => item.Status == RecordStatus.Publiee);
    public int HealthIssueCount => Components.Count(item => !item.IsNameCompliant || item.IsMissing) + Furniture.Count(item => item.ComponentIds.Count == 0);
    public string LastModification => _catalog.Revision == 0 ? "Données de démonstration" : $"Révision {_catalog.Revision} · {_catalog.ModifiedBy}";

    public string ComponentSearch { get => _componentSearch; set { if (SetProperty(ref _componentSearch, value)) ComponentView.Refresh(); } }
    public string FurnitureSearch { get => _furnitureSearch; set { if (SetProperty(ref _furnitureSearch, value)) FurnitureView.Refresh(); } }
    public string ClientSearch { get => _clientSearch; set { if (SetProperty(ref _clientSearch, value)) ClientFurnitureView.Refresh(); } }
    public ComponentRecord? SelectedComponent { get => _selectedComponent; set { if (SetProperty(ref _selectedComponent, value)) RaiseCommandStates(); } }
    public FurnitureRecord? SelectedFurniture { get => _selectedFurniture; set { if (SetProperty(ref _selectedFurniture, value)) { RefreshLinkedComponents(); RaiseCommandStates(); } } }
    public ComponentRecord? SelectedCompositionCandidate { get => _selectedCompositionCandidate; set { if (SetProperty(ref _selectedCompositionCandidate, value)) RaiseCommandStates(); } }
    public ComponentRecord? SelectedLinkedComponent { get => _selectedLinkedComponent; set { if (SetProperty(ref _selectedLinkedComponent, value)) RaiseCommandStates(); } }
    public FurnitureRecord? SelectedClientFurniture { get => _selectedClientFurniture; set => SetProperty(ref _selectedClientFurniture, value); }
    public string InheritedCapabilities => JoinInherited(item => item.CapabilitiesCsv);
    public string InheritedCompatibility => JoinInherited(item => item.CompatibilityCsv);

    public RelayCommand NavigateCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand ChooseLibraryCommand { get; }
    public RelayCommand ChooseSharedRootCommand { get; }
    public RelayCommand SaveBootstrapCommand { get; }
    public RelayCommand ValidateComponentCommand { get; }
    public RelayCommand CreateFurnitureCommand { get; }
    public RelayCommand AddComponentCommand { get; }
    public RelayCommand RemoveComponentCommand { get; }
    public RelayCommand PublishFurnitureCommand { get; }
    public RelayCommand OpenSharedRootCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }

    public async Task InitializeAsync()
    {
        await ReloadAsync(false);
        foreach (var user in await _userStore.LoadAsync()) Users.Add(user);
    }

    public async Task CheckAutoUpdateAsync()
    {
        if (Settings.AutoUpdate) await CheckUpdateAsync(true);
    }

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
        var account = await _userStore.AddAsync(login, displayName, password, permissions);
        Users.Add(account);
    }

    private async Task ReloadAsync(bool showMessage = true)
    {
        IsBusy = true;
        try
        {
            _catalog = await _store.LoadAsync();
            Components.Clear();
            foreach (var component in _catalog.Components) Components.Add(component);
            Furniture.Clear();
            foreach (var furniture in _catalog.Furniture) Furniture.Add(furniture);
            SelectedComponent = Components.FirstOrDefault();
            SelectedFurniture = Furniture.FirstOrDefault();
            SelectedClientFurniture = Furniture.FirstOrDefault(item => item.Status == RecordStatus.Publiee);
            NotifySummary();
            StatusText = showMessage ? "Catalogue rechargé." : LastModification;
        }
        catch (Exception exception) { ShowError(exception); }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            _catalog.Components = Components.ToList();
            _catalog.Furniture = Furniture.ToList();
            await _store.SaveAsync(_catalog, _catalog.Revision, CurrentUser.DisplayName);
            StatusText = $"Enregistré · révision {_catalog.Revision}";
            NotifySummary();
        }
        catch (CatalogConcurrencyException exception)
        {
            MessageBox.Show(exception.Message, "Modification concurrente", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception) { ShowError(exception); }
        finally { IsBusy = false; }
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.LibraryRoot)) ChooseLibrary();
        if (string.IsNullOrWhiteSpace(Settings.LibraryRoot)) return;
        IsBusy = true;
        StatusText = "Analyse de la bibliothèque…";
        try
        {
            var scan = await _scanner.ScanAsync(Settings.LibraryRoot);
            foreach (var existing in Components.Where(item => !item.IsDemo)) existing.IsMissing = true;
            if (scan.Components.Count > 0)
            {
                foreach (var demo in Components.Where(item => item.IsDemo).ToArray()) Components.Remove(demo);
                foreach (var demo in Furniture.Where(item => item.IsDemo).ToArray()) Furniture.Remove(demo);
            }
            foreach (var item in scan.Components)
            {
                var record = Components.FirstOrDefault(component => component.Id == item.StableId);
                if (record is null)
                {
                    record = new ComponentRecord { Id = item.StableId, DisplayName = item.TechnicalName, Status = RecordStatus.Brouillon };
                    Components.Add(record);
                }
                record.SourceRelativePath = item.RelativeTopPath;
                record.PreviewRelativePath = item.PreviewRelativePath;
                record.LibraryName = item.Library;
                record.FamilyName = item.Family;
                record.TechnicalName = item.TechnicalName;
                record.TypeCode = item.Parsed.Type;
                record.VariantCode = item.Parsed.Variant;
                record.IndexCode = item.Parsed.Index;
                record.RangeCode = item.Parsed.Range;
                record.ConstructionCode = item.Parsed.Construction;
                record.IsNameCompliant = item.IsCompliant;
                record.IsMissing = false;
                record.LastSeenUtc = DateTimeOffset.UtcNow;
            }
            ComponentView.Refresh();
            NotifySummary();
            StatusText = $"{scan.Components.Count:N0} composants indexés · {scan.Warnings.Count} avertissement(s).";
        }
        catch (Exception exception) { ShowError(exception); }
        finally { IsBusy = false; }
    }

    private void ValidateComponent()
    {
        if (SelectedComponent is null) return;
        if (!SelectedComponent.IsNameCompliant && string.IsNullOrWhiteSpace(SelectedComponent.ForcedValidationReason))
        {
            MessageBox.Show("Ce nom n’est pas conforme. Renseignez le motif de validation forcée avant de valider.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedComponent.Status = RecordStatus.Validee;
        SelectedComponent.ValidatedBy = CurrentUser.DisplayName;
        SelectedComponent.ValidatedUtc = DateTimeOffset.UtcNow;
        StatusText = $"Fiche validée par {CurrentUser.DisplayName}. Pensez à enregistrer.";
        ComponentView.Refresh();
    }

    private void CreateFurniture()
    {
        var item = new FurnitureRecord { Reference = $"MEU-{Furniture.Count(item => !item.IsDemo) + 1:0000}", DisplayName = "Nouveau meuble", Status = RecordStatus.Brouillon };
        Furniture.Add(item);
        SelectedFurniture = item;
        FurnitureView.Refresh();
        CurrentPage = "Furniture";
        NotifySummary();
    }

    private void AddComponent()
    {
        if (SelectedFurniture is null || SelectedCompositionCandidate is null || SelectedFurniture.ComponentIds.Contains(SelectedCompositionCandidate.Id)) return;
        SelectedFurniture.ComponentIds.Add(SelectedCompositionCandidate.Id);
        RefreshLinkedComponents();
    }

    private void RemoveComponent()
    {
        if (SelectedFurniture is null || SelectedLinkedComponent is null) return;
        SelectedFurniture.ComponentIds.Remove(SelectedLinkedComponent.Id);
        RefreshLinkedComponents();
    }

    private void PublishFurniture()
    {
        if (SelectedFurniture is null) return;
        var invalid = LinkedComponents.Where(item => item.Status is not (RecordStatus.Validee or RecordStatus.Retenue or RecordStatus.Publiee)).ToArray();
        if ((LinkedComponents.Count == 0 || invalid.Length > 0) && string.IsNullOrWhiteSpace(SelectedFurniture.ForcedValidationReason))
        {
            MessageBox.Show("La composition est vide ou contient des composants non validés. Renseignez le motif de forçage pour conserver le dernier mot.", "Publication", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedFurniture.Status = RecordStatus.Publiee;
        SelectedFurniture.ValidatedBy = CurrentUser.DisplayName;
        SelectedFurniture.ValidatedUtc = DateTimeOffset.UtcNow;
        ClientFurnitureView.Refresh();
        NotifySummary();
        StatusText = "Meuble publié dans l’aperçu du Catalogue Atlas. Pensez à enregistrer.";
    }

    private void RefreshLinkedComponents()
    {
        LinkedComponents.Clear();
        if (SelectedFurniture is not null)
            foreach (var id in SelectedFurniture.ComponentIds)
                if (Components.FirstOrDefault(item => item.Id == id) is { } component) LinkedComponents.Add(component);
        OnPropertyChanged(nameof(InheritedCapabilities));
        OnPropertyChanged(nameof(InheritedCompatibility));
    }

    private string JoinInherited(Func<ComponentRecord, string> selector) => string.Join(", ", LinkedComponents
        .SelectMany(item => selector(item).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

    private bool FilterComponent(object item)
    {
        if (item is not ComponentRecord component || string.IsNullOrWhiteSpace(ComponentSearch)) return true;
        var query = ComponentSearch.Trim();
        return new[] { component.DisplayName, component.TechnicalName, component.LibraryName, component.FamilyName, component.TypeCode }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterFurniture(object item)
    {
        if (item is not FurnitureRecord furniture || string.IsNullOrWhiteSpace(FurnitureSearch)) return true;
        var query = FurnitureSearch.Trim();
        return new[] { furniture.Reference, furniture.DisplayName, furniture.Family, furniture.Description }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterClientFurniture(object item)
    {
        if (item is not FurnitureRecord furniture || furniture.Status != RecordStatus.Publiee) return false;
        if (string.IsNullOrWhiteSpace(ClientSearch)) return true;
        var query = ClientSearch.Trim();
        return new[] { furniture.Reference, furniture.DisplayName, furniture.Family, furniture.Description, furniture.UseCasesCsv }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void ChooseLibrary()
    {
        var dialog = new OpenFolderDialog { Title = "Choisir la bibliothèque TopSolid", Multiselect = false };
        if (Directory.Exists(Settings.LibraryRoot)) dialog.InitialDirectory = Settings.LibraryRoot;
        if (dialog.ShowDialog() == true) Settings.LibraryRoot = dialog.FolderName;
    }

    private void ChooseSharedRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choisir le dossier partagé Atlas", Multiselect = false };
        if (Directory.Exists(SharedRoot)) dialog.InitialDirectory = SharedRoot;
        if (dialog.ShowDialog() == true) SharedRoot = dialog.FolderName;
    }

    private async Task SaveBootstrapAsync()
    {
        _bootstrap.SharedRoot = SharedRoot;
        await SharedCatalogStore.SaveBootstrapAsync(_bootstrap);
        MessageBox.Show("Le nouvel emplacement sera utilisé au prochain lancement.", "Dossier partagé", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSharedRoot()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _store.SharedRoot) { UseShellExecute = true }); }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task CheckUpdateAsync(bool silent)
    {
        IsBusy = true;
        UpdateLabel = "Vérification…";
        try
        {
            var available = await _updater.CheckAsync();
            if (available is null)
            {
                UpdateLabel = "Application à jour";
                if (!silent) MessageBox.Show("Vous utilisez la dernière version.", "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            UpdateLabel = $"Installer {available}";
            if (MessageBox.Show($"La version {available} est disponible. L’installer maintenant ?", "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await _updater.DownloadAndRestartAsync(progress => UpdateLabel = $"Téléchargement {progress}%");
        }
        catch (Exception exception)
        {
            UpdateLabel = "Mise à jour indisponible";
            if (!silent) ShowError(exception);
        }
        finally { IsBusy = false; }
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(EnvironmentLabel));
        OnPropertyChanged(nameof(ComponentCount));
        OnPropertyChanged(nameof(FurnitureCount));
        OnPropertyChanged(nameof(PublishedCount));
        OnPropertyChanged(nameof(HealthIssueCount));
        OnPropertyChanged(nameof(LastModification));
        ClientFurnitureView.Refresh();
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { SaveCommand, ReloadCommand, ScanCommand, ValidateComponentCommand, AddComponentCommand, RemoveComponentCommand, PublishFurnitureCommand, CheckUpdateCommand })
            command.RaiseCanExecuteChanged();
    }

    private static void ShowError(Exception exception) => MessageBox.Show(exception.Message, "Biblidéo Atlas", MessageBoxButton.OK, MessageBoxImage.Error);
}
