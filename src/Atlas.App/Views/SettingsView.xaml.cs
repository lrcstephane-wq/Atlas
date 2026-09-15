using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Atlas.App.ViewModels;
using Atlas.Core.Models;
using Atlas.Core.Services;

namespace Atlas.App.Views;

public partial class SettingsView : UserControl
{
    private readonly ObservableCollection<ComponentFamilyRecord> _families = [];
    private readonly ObservableCollection<ComponentTagRecord> _tags = [];
    private readonly ObservableCollection<ToggleOptionViewModel> _familyTagChoices = [];
    private readonly ObservableCollection<ToggleOptionViewModel> _tagFamilyChoices = [];
    private readonly ObservableCollection<string> _universes = [];
    private readonly ObservableCollection<string> _familyLibraries = ["Toutes les bibliothèques"];
    private ComponentTaxonomy _taxonomy = new();
    private Dictionary<string, HashSet<string>> _savedAssignments = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshing;
    private Button? _activeSettingsButton;

    public SettingsView()
    {
        InitializeComponent();
        FamilyList.ItemsSource = _families;
        TagList.ItemsSource = _tags;
        FamilyTagChoices.ItemsSource = _familyTagChoices;
        TagFamilyChoices.ItemsSource = _tagFamilyChoices;
        UniverseList.ItemsSource = _universes;
        FamilyLibraryFilter.ItemsSource = _familyLibraries;
        FamilyLibraryFilter.SelectedIndex = 0;
        TagFamilyLibraryFilter.ItemsSource = _familyLibraries;
        TagFamilyLibraryFilter.SelectedIndex = 0;
        CollectionViewSource.GetDefaultView(_families).Filter = FilterFamily;
    }

    private async void SettingsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_families.Count > 0 || _tags.Count > 0) return;
        await ReloadTaxonomyAsync();
        if (DataContext is MainViewModel vm) foreach (var universe in vm.CatalogUniverses.Order(StringComparer.CurrentCultureIgnoreCase)) _universes.Add(universe);
        ActivateSettingsButton(GeneralSettingsButton);
        SetTaxonomyMode("Family");
    }

    private async Task ReloadTaxonomyAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _taxonomy = await ComponentTaxonomyStore.LoadAsync(vm.SharedRoot);
        var added = ComponentTaxonomyStore.SyncDetectedFamilies(_taxonomy, vm.Components);
        RefreshCollections();
        _savedAssignments = _taxonomy.Families.ToDictionary(x => x.Id, x => x.TagIds.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        TaxonomyStatus.Text = added == 0 ? $"{_families.Count} famille(s) · {_tags.Count} tag(s)." : $"{added} nouvelle(s) famille(s) détectée(s). Enregistrez pour les partager.";
    }

    private void RefreshCollections()
    {
        _refreshing = true;
        var selectedFamilyId = (FamilyList.SelectedItem as ComponentFamilyRecord)?.Id;
        var selectedTagId = (TagList.SelectedItem as ComponentTagRecord)?.Id;
        _families.Clear();
        foreach (var family in _taxonomy.Families.OrderBy(x => x.LibraryName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) _families.Add(family);
        _tags.Clear();
        foreach (var tag in _taxonomy.Tags.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)) _tags.Add(tag);
        FamilyList.SelectedItem = _families.FirstOrDefault(x => x.Id == selectedFamilyId) ?? _families.FirstOrDefault();
        TagList.SelectedItem = _tags.FirstOrDefault(x => x.Id == selectedTagId) ?? _tags.FirstOrDefault();
        if (DataContext is MainViewModel vm)
        {
            var libraries = vm.Components.Select(x => x.LibraryName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (libraries.Count == 0) libraries.Add("Atlas");
            NewFamilyLibrary.ItemsSource = libraries;
            NewFamilyLibrary.SelectedIndex = 0;
            var selectedLibrary = FamilyLibraryFilter.SelectedItem?.ToString();
            _familyLibraries.Clear();
            _familyLibraries.Add("Toutes les bibliothèques");
            foreach (var library in libraries) _familyLibraries.Add(library);
            FamilyLibraryFilter.SelectedItem = _familyLibraries.FirstOrDefault(x => x.Equals(selectedLibrary, StringComparison.OrdinalIgnoreCase)) ?? _familyLibraries[0];
            TagFamilyLibraryFilter.SelectedItem ??= _familyLibraries[0];
        }
        _refreshing = false;
        RefreshFamilyTagChoices();
        RefreshTagFamilyChoices();
    }

    private void SettingsNavigation_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } button) return;
        ActivateSettingsButton(button);
        foreach (var panel in new FrameworkElement[] { GeneralPanel, UsersPanel, LibrariesPanel, TaxonomyPanel, CompatibilityPanel, FurniturePanel, ValidationPanel, CapabilitiesPanel, ClientPanel, ClientSearchPanel, TopSolidPanel, SystemPanel })
            panel.Visibility = panel.Name == target ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ActivateSettingsButton(Button button)
    {
        if (_activeSettingsButton is not null)
        {
            _activeSettingsButton.ClearValue(BackgroundProperty);
            _activeSettingsButton.ClearValue(BorderBrushProperty);
        }
        _activeSettingsButton = button;
        button.Background = new SolidColorBrush(Color.FromRgb(33, 78, 134));
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191));
    }

    private void TaxonomyMode_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string mode }) SetTaxonomyMode(mode);
    }

    private void SetTaxonomyMode(string mode)
    {
        var familyMode = mode == "Family";
        FamilyListColumn.Width = familyMode ? new GridLength(370) : new GridLength(0);
        FamilyGapColumn.Width = familyMode ? new GridLength(12) : new GridLength(0);
        FamilyDetailColumn.Width = familyMode ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TagGapColumn.Width = new GridLength(0);
        TagColumn.Width = familyMode ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        FamilyModeButton.Background = new SolidColorBrush(familyMode ? Color.FromRgb(45, 126, 247) : Color.FromRgb(24, 40, 62));
        TagModeButton.Background = new SolidColorBrush(familyMode ? Color.FromRgb(24, 40, 62) : Color.FromRgb(45, 126, 247));
    }

    private void FamilySelection_OnChanged(object sender, SelectionChangedEventArgs e) { if (!_refreshing) RefreshFamilyTagChoices(); }
    private void TagSelection_OnChanged(object sender, SelectionChangedEventArgs e) { if (!_refreshing) RefreshTagFamilyChoices(); }

    private void FamilyFilter_OnChanged(object sender, EventArgs e)
    {
        if (!IsLoaded) return;
        CollectionViewSource.GetDefaultView(_families).Refresh();
    }

    private void TagFamilyFilter_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_refreshing) RefreshTagFamilyChoices();
    }

    private bool FilterFamily(object item)
    {
        if (item is not ComponentFamilyRecord family) return false;
        var library = FamilyLibraryFilter.SelectedItem?.ToString() ?? "Toutes les bibliothèques";
        if (library != "Toutes les bibliothèques" && !family.LibraryName.Equals(library, StringComparison.OrdinalIgnoreCase)) return false;
        var query = FamilySearch.Text.Trim();
        return string.IsNullOrWhiteSpace(query) || family.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || family.LibraryName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFamilyTagChoices()
    {
        _refreshing = true;
        _familyTagChoices.Clear();
        if (FamilyList.SelectedItem is ComponentFamilyRecord family)
            foreach (var tag in _tags.Where(x => x.IsActive))
                _familyTagChoices.Add(new ToggleOptionViewModel(tag.Label, family.TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase), choice =>
                {
                    SetMembership(family.TagIds, tag.Id, choice.IsSelected);
                    TaxonomyStatus.Text = "Affectation modifiée. Enregistrez pour la propager.";
                    RefreshTagFamilyChoices();
                }));
        _refreshing = false;
    }

    private void RefreshTagFamilyChoices()
    {
        _refreshing = true;
        _tagFamilyChoices.Clear();
        if (TagList.SelectedItem is ComponentTagRecord tag)
        {
            var library = TagFamilyLibraryFilter.SelectedItem?.ToString() ?? "Toutes les bibliothèques";
            foreach (var family in _families.Where(x => x.IsActive && (library == "Toutes les bibliothèques" || x.LibraryName.Equals(library, StringComparison.OrdinalIgnoreCase))))
                _tagFamilyChoices.Add(new ToggleOptionViewModel(family.QualifiedName, family.TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase), choice =>
                {
                    SetMembership(family.TagIds, tag.Id, choice.IsSelected);
                    TaxonomyStatus.Text = "Affectation modifiée. Enregistrez pour la propager.";
                    RefreshFamilyTagChoices();
                }));
        }
        _refreshing = false;
    }

    private void AddFamily_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true }) return;
        var name = NewFamilyName.Text.Trim();
        var library = NewFamilyLibrary.SelectedItem?.ToString() ?? "Atlas";
        if (string.IsNullOrWhiteSpace(name)) { TaxonomyStatus.Text = "Renseignez le nom de la famille."; return; }
        if (_families.Any(x => ComponentTaxonomyStore.FamilyKey(x.LibraryName, x.Name).Equals(ComponentTaxonomyStore.FamilyKey(library, name), StringComparison.OrdinalIgnoreCase))) { TaxonomyStatus.Text = "Cette famille existe déjà dans cette bibliothèque."; return; }
        var family = new ComponentFamilyRecord { LibraryName = library, Name = name, IsDetected = false };
        _taxonomy.Families.Add(family); NewFamilyName.Clear(); RefreshCollections(); FamilyList.SelectedItem = family;
        TaxonomyStatus.Text = "Famille manuelle créée. Affectez-lui ses tags puis enregistrez.";
    }

    private void AddTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true }) return;
        var label = NewTagName.Text.Trim();
        if (string.IsNullOrWhiteSpace(label)) { TaxonomyStatus.Text = "Saisissez le libellé du nouveau tag."; return; }
        if (_tags.Any(x => x.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) { TaxonomyStatus.Text = "Ce tag existe déjà."; return; }
        var tag = new ComponentTagRecord { Label = label, Category = "Autre" };
        _taxonomy.Tags.Add(tag); NewTagName.Clear(); RefreshCollections(); TagList.SelectedItem = tag;
        TaxonomyStatus.Text = "Tag créé. Choisissez sa catégorie et les familles auxquelles l’affecter.";
    }

    private void DeleteTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } || TagList.SelectedItem is not ComponentTagRecord tag) return;
        var affected = _families.Count(x => x.TagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase));
        if (!AtlasDialog.Confirm($"Supprimer le tag « {tag.Label} » ?", "Suppression du tag", $"{affected} famille(s) le référencent actuellement.")) return;
        _taxonomy.Tags.Remove(tag);
        foreach (var family in _taxonomy.Families) RemoveIgnoreCase(family.TagIds, tag.Id);
        RefreshCollections();
        TaxonomyStatus.Text = "Tag supprimé. Enregistrez pour confirmer.";
    }

    private void SyncFamilies_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var added = ComponentTaxonomyStore.SyncDetectedFamilies(_taxonomy, vm.Components);
        RefreshCollections();
        TaxonomyStatus.Text = added == 0 ? "Les familles détectées sont déjà synchronisées." : $"{added} nouvelle(s) famille(s) trouvée(s). Enregistrez pour les partager.";
    }

    private async void SaveTaxonomy_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        var duplicateTags = _tags.Where(x => !string.IsNullOrWhiteSpace(x.Label)).GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicateTags is not null) { TaxonomyStatus.Text = $"Tag en double : {duplicateTags.Key}"; return; }
        if (_tags.Any(x => string.IsNullOrWhiteSpace(x.Label)) || _families.Any(x => string.IsNullOrWhiteSpace(x.Name))) { TaxonomyStatus.Text = "Chaque famille et chaque tag doit avoir un nom."; return; }

        var changedFamilies = _families.Where(f => !_savedAssignments.TryGetValue(f.Id, out var saved) || !saved.SetEquals(f.TagIds)).ToArray();
        var impacted = vm.Components.Count(component => changedFamilies.Any(f => ComponentTaxonomyStore.FamilyKey(f.LibraryName, f.Name).Equals(ComponentTaxonomyStore.FamilyKey(component.LibraryName, component.EffectiveFamilyName), StringComparison.OrdinalIgnoreCase)));
        if (impacted > 0 && !AtlasDialog.Confirm("Appliquer ces changements de tags ?", "Propagation aux composants", $"{changedFamilies.Length} famille(s) modifiée(s) · {impacted} composant(s) verront leurs tags hérités évoluer. Les exceptions manuelles seront conservées.")) return;

        foreach (var tag in _tags) { tag.Label = tag.Label.Trim(); tag.Description = tag.Description.Trim(); }
        foreach (var family in _families) { family.Name = family.Name.Trim(); family.Description = family.Description.Trim(); }
        await ComponentTaxonomyStore.SaveAsync(vm.SharedRoot, _taxonomy);
        await vm.ReloadTaxonomyAsync();
        _savedAssignments = _taxonomy.Families.ToDictionary(x => x.Id, x => x.TagIds.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        TaxonomyStatus.Text = $"Référentiel enregistré · {_families.Count} famille(s) · {_tags.Count} tag(s).";
        vm.StatusText = "Familles et tags partagés enregistrés.";
    }

    private async void CreateUser_OnClick(object sender, RoutedEventArgs e)
    {
        UserError.Text = string.Empty;
        try
        {
            if (DataContext is not MainViewModel viewModel) return;
            await viewModel.AddUserAsync(NewLogin.Text, NewDisplayName.Text, NewPassword.Password, NewRole.SelectedItem?.ToString() ?? "Lecture seule");
            NewLogin.Clear(); NewDisplayName.Clear(); NewPassword.Clear();
        }
        catch (Exception exception) { UserError.Text = exception.Message; }
    }

    private void AddUniverseSetting_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        var name = NewUniverseSetting.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || _universes.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        _universes.Add(name); NewUniverseSetting.Clear(); vm.ReplaceUniverses(_universes);
    }

    private void DeleteUniverseSetting_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || UniverseList.SelectedItem is not string selected) return;
        var count = vm.Furniture.Count(x => x.Universes.Contains(selected, StringComparer.OrdinalIgnoreCase));
        if (!AtlasDialog.Confirm($"Supprimer l’univers « {selected} » ?", "Suppression d’un univers", $"{count} meuble(s) utilisent encore cet univers. Leur fiche conservera la valeur jusqu’à modification manuelle.")) return;
        _universes.Remove(selected); vm.ReplaceUniverses(_universes);
    }

    private static void SetMembership(List<string> values, string id, bool selected)
    {
        if (selected && !values.Contains(id, StringComparer.OrdinalIgnoreCase)) values.Add(id);
        if (!selected) RemoveIgnoreCase(values, id);
    }

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }
}
