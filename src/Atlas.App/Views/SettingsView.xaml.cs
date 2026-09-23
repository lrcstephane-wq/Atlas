using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atlas.App.ViewModels;
using Atlas.Core.Models;
using Atlas.Core.Services;
using Microsoft.Win32;

namespace Atlas.App.Views;

public partial class SettingsView : UserControl
{
    private readonly ObservableCollection<ComponentTagRecord> _tags = [];
    private readonly ObservableCollection<string> _tagCategoryFilters = ["Toutes les catégories"];
    private readonly ObservableCollection<CatalogUniverseRecord> _universes = [];
    private ComponentTaxonomy _taxonomy = new();
    private Button? _activeSettingsButton;

    public SettingsView()
    {
        InitializeComponent();
        TagList.ItemsSource = _tags;
        TagCategoryFilter.ItemsSource = _tagCategoryFilters;
        TagCategoryFilter.SelectedIndex = 0;
        UniverseList.ItemsSource = _universes;
        CollectionViewSource.GetDefaultView(_tags).Filter = FilterTag;
    }

    private async void SettingsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_tags.Count > 0) return;
        await ReloadTaxonomyAsync();
        if (DataContext is MainViewModel vm)
        {
            foreach (var universe in vm.CatalogUniverseDefinitions.OrderBy(item => item.SortOrder)) _universes.Add(universe);
            foreach (var category in vm.TagCategories.Where(category => !_tagCategoryFilters.Contains(category, StringComparer.OrdinalIgnoreCase))) _tagCategoryFilters.Add(category);
        }
        if (DataContext is MainViewModel vm && (vm.IsUserMode || vm.IsLicenseRestricted))
        {
            ActivateSettingsButton(LicenseSettingsButton);
            ShowSettingsPanel("LicensePanel");
        }
        else ActivateSettingsButton(GeneralSettingsButton);
    }

    private async Task ReloadTaxonomyAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _taxonomy = await ComponentTaxonomyStore.LoadAsync(vm.SharedRoot);
        RefreshCollections();
        TaxonomyStatus.Text = $"{_tags.Count} tag(s) disponible(s).";
    }

    private void AddSearchSynonym_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        var item = new SearchSynonymRecord { Canonical = "Nouveau concept" };
        vm.Settings.SearchSynonyms.Add(item);
        SearchSynonymGrid.Items.Refresh();
        SearchSynonymGrid.SelectedItem = item;
        SearchSynonymGrid.ScrollIntoView(item);
        SearchDictionaryStatus.Text = "Nouvelle ligne ajoutée. Renseignez ses synonymes puis enregistrez.";
    }

    private void DeleteSearchSynonym_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || SearchSynonymGrid.SelectedItem is not SearchSynonymRecord item) return;
        vm.Settings.SearchSynonyms.Remove(item);
        SearchSynonymGrid.Items.Refresh();
        SearchDictionaryStatus.Text = "Ligne retirée. Enregistrez pour confirmer.";
    }

    private async void SaveSearchDictionary_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        SearchSynonymGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SearchSynonymGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var item in vm.Settings.SearchSynonyms)
        {
            item.Canonical = (item.Canonical ?? string.Empty).Trim();
            item.AliasesCsv = (item.AliasesCsv ?? string.Empty).Trim();
        }
        if (vm.Settings.SearchSynonyms.Any(item => string.IsNullOrWhiteSpace(item.Canonical)))
        {
            SearchDictionaryStatus.Text = "Chaque ligne doit avoir un concept principal.";
            return;
        }
        var duplicate = vm.Settings.SearchSynonyms.GroupBy(item => item.Canonical, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            SearchDictionaryStatus.Text = $"Concept en double : {duplicate.Key}.";
            return;
        }
        if (!await vm.SaveCatalogAsync()) return;
        vm.RefreshHorizonSearchSettings();
        SearchDictionaryStatus.Text = "Dictionnaire enregistré et immédiatement actif dans Horizon.";
    }

    private void RefreshCollections()
    {
        var selectedTagId = (TagList.SelectedItem as ComponentTagRecord)?.Id;
        _tags.Clear();
        foreach (var tag in _taxonomy.Tags.OrderBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)) _tags.Add(tag);
        TagList.SelectedItem = _tags.FirstOrDefault(x => x.Id == selectedTagId) ?? _tags.FirstOrDefault();
    }

    private void SettingsNavigation_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target } button) return;
        ActivateSettingsButton(button);
        ShowSettingsPanel(target);
    }

    private void ShowSettingsPanel(string target)
    {
        foreach (var panel in new FrameworkElement[] { LicensePanel, GeneralPanel, UsersPanel, LibrariesPanel, TaxonomyPanel, CompatibilityPanel, FurniturePanel, ValidationPanel, CapabilitiesPanel, ClientPanel, ClientSearchPanel, TopSolidPanel, SystemPanel })
            panel.Visibility = panel.Name == target ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstallLicense_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var result = vm.InstallLicense();
        LicenseInstallStatus.Text = result.Message;
        if (result.IsValid) AtlasDialog.Info($"Licence activée pour {result.Customer} jusqu’au {result.ValidUntil:dd/MM/yyyy} inclus.", "Licence Atlas");
        else AtlasDialog.Warning(result.Message, "Licence non activée");
    }

    private void GenerateLicense_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            vm.GenerateLicense();
            LicenseGeneratorStatus.Text = $"Code prêt pour {vm.NewLicenseCustomer.Trim()}, valable jusqu’au {vm.NewLicenseValidUntil:dd/MM/yyyy} inclus.";
        }
        catch (Exception exception)
        {
            LicenseGeneratorStatus.Text = exception.Message;
            AtlasDialog.Warning(exception.Message, "Licence non générée");
        }
    }

    private void CopyGeneratedLicense_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || string.IsNullOrWhiteSpace(vm.GeneratedLicenseCode)) return;
        Clipboard.SetText(vm.GeneratedLicenseCode);
        LicenseGeneratorStatus.Text = "Code copié dans le presse-papiers.";
    }

    private void InstallLicenseSigningKey_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dialog = new OpenFileDialog { Title = "Installer la clé privée Idéo", Filter = "Clé privée PEM (*.pem)|*.pem|Tous les fichiers (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            vm.InstallLicenseSigningKey(dialog.FileName);
            LicenseGeneratorStatus.Text = "Clé privée Idéo installée. Le générateur est opérationnel.";
        }
        catch (Exception exception)
        {
            LicenseGeneratorStatus.Text = exception.Message;
            AtlasDialog.Warning(exception.Message, "Clé refusée");
        }
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

    private void TagSearch_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) CollectionViewSource.GetDefaultView(_tags).Refresh();
    }

    private void TagCategoryFilter_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) CollectionViewSource.GetDefaultView(_tags).Refresh();
    }

    private bool FilterTag(object item)
    {
        if (item is not ComponentTagRecord tag) return false;
        var category = TagCategoryFilter?.SelectedItem?.ToString() ?? "Toutes les catégories";
        if (category != "Toutes les catégories" && !tag.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) return false;
        var query = TagSearch?.Text.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(query)
            || (tag.Label ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
            || (tag.Category ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
            || (tag.Description ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async void AddTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        var label = NewTagName.Text.Trim();
        if (string.IsNullOrWhiteSpace(label)) { TaxonomyStatus.Text = "Saisissez le libellé du nouveau tag."; return; }
        if (_tags.Any(x => x.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) { TaxonomyStatus.Text = "Ce tag existe déjà."; return; }
        var tag = new ComponentTagRecord { Label = label, Category = "Autre" };
        _taxonomy.Tags.Add(tag); NewTagName.Clear(); RefreshCollections(); TagList.SelectedItem = tag;
        if (!await PersistTagsAsync(vm, $"Tag « {tag.Label} » créé et enregistré."))
        {
            _taxonomy.Tags.Remove(tag);
            RefreshCollections();
        }
    }

    private async void DeleteTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || TagList.SelectedItem is not ComponentTagRecord tag) return;
        var affected = vm.Components.Count(x => x.AddedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase));
        if (!AtlasDialog.Confirm($"Supprimer le tag « {tag.Label} » ?", "Suppression du tag", $"{affected} composant(s) utilisent actuellement ce tag. Il disparaîtra de leurs filtres.")) return;
        _taxonomy.Tags.Remove(tag);
        RefreshCollections();
        if (!await PersistTagsAsync(vm, $"Tag « {tag.Label} » supprimé définitivement."))
        {
            _taxonomy.Tags.Add(tag);
            RefreshCollections();
            TagList.SelectedItem = tag;
        }
    }

    private async void SaveTaxonomy_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        await PersistTagsAsync(vm, "Modifications du tag enregistrées.");
    }

    private async Task<bool> PersistTagsAsync(MainViewModel vm, string successMessage)
    {
        var duplicateTags = _tags.Where(x => !string.IsNullOrWhiteSpace(x.Label)).GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicateTags is not null) { TaxonomyStatus.Text = $"Tag en double : {duplicateTags.Key}"; return false; }
        if (_tags.Any(x => string.IsNullOrWhiteSpace(x.Label))) { TaxonomyStatus.Text = "Chaque tag doit avoir un libellé."; return false; }

        try
        {
            foreach (var tag in _tags) { tag.Label = tag.Label.Trim(); tag.Description = (tag.Description ?? string.Empty).Trim(); }
            await ComponentTaxonomyStore.SaveAsync(vm.SharedRoot, _taxonomy);
            await vm.ReloadTaxonomyAsync();
            RefreshCollections();
            TaxonomyStatus.Text = successMessage;
            vm.StatusText = successMessage;
            return true;
        }
        catch (Exception exception)
        {
            TaxonomyStatus.Text = "Enregistrement impossible.";
            AtlasDialog.Error(exception.Message, "Enregistrement des tags impossible", "Vérifiez le dossier partagé Atlas dans Paramètres › Espace de travail.");
            return false;
        }
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
        if (string.IsNullOrWhiteSpace(name) || _universes.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        var universe = new CatalogUniverseRecord { Name = name, SortOrder = _universes.Count, Description = "Collection de meubles dédiée" };
        _universes.Add(universe); NewUniverseSetting.Clear(); UniverseList.SelectedItem = universe; vm.ReplaceUniverseDefinitions(_universes);
    }

    private void DeleteUniverseSetting_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || UniverseList.SelectedItem is not CatalogUniverseRecord selected) return;
        var count = vm.Furniture.Count(x => x.Universes.Contains(selected.Name, StringComparer.OrdinalIgnoreCase));
        if (!AtlasDialog.Confirm($"Supprimer l’univers « {selected.Name} » ?", "Suppression d’un univers", $"{count} meuble(s) utilisent encore cet univers. Leur fiche conservera la valeur jusqu’à modification manuelle.")) return;
        _universes.Remove(selected); vm.ReplaceUniverseDefinitions(_universes);
    }

    private void MoveUniverseUp_OnClick(object sender, RoutedEventArgs e) => MoveUniverse(-1);
    private void MoveUniverseDown_OnClick(object sender, RoutedEventArgs e) => MoveUniverse(1);

    private void MoveUniverse(int direction)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || UniverseList.SelectedItem is not CatalogUniverseRecord selected) return;
        var index = _universes.IndexOf(selected);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _universes.Count) return;
        _universes.Move(index, target);
        for (var i = 0; i < _universes.Count; i++) _universes[i].SortOrder = i;
        vm.ReplaceUniverseDefinitions(_universes);
        UniverseList.SelectedItem = selected;
    }

    private void ChooseUniverseImage_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || UniverseList.SelectedItem is not CatalogUniverseRecord selected) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Image de l’univers {selected.Name}",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Tous les fichiers (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var folder = Path.Combine(vm.SharedRoot, "Images", "Universes");
            Directory.CreateDirectory(folder);
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var destination = Path.Combine(folder, $"{selected.Id}{extension}");
            File.Copy(dialog.FileName, destination, true);
            selected.ImageRelativePath = Path.GetRelativePath(vm.SharedRoot, destination);
            vm.ReplaceUniverseDefinitions(_universes);
            UniverseImagePreview.Source = LoadPreview(destination);
            UniverseStatus.Text = $"Image associée à « {selected.Name} ». Enregistrez les univers pour la partager.";
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Image impossible à associer", "Vérifiez l’accès au dossier partagé Atlas.");
        }
    }

    private async void SaveUniverses_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        if (_universes.Any(item => string.IsNullOrWhiteSpace(item.Name))) { UniverseStatus.Text = "Chaque univers doit avoir un nom."; return; }
        var duplicate = _universes.GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) { UniverseStatus.Text = $"Univers en double : {duplicate.Key}."; return; }
        vm.ReplaceUniverseDefinitions(_universes);
        if (!await vm.SaveCatalogAsync()) return;
        vm.RefreshUniversePresentation();
        UniverseStatus.Text = "Univers enregistrés et immédiatement disponibles dans Horizon.";
    }

    private void FurnitureTypeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FurnitureTypeName.Text = FurnitureTypeList.SelectedItem?.ToString() ?? string.Empty;
    }

    private void FurnitureUsageList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FurnitureUsageName.Text = FurnitureUsageList.SelectedItem?.ToString() ?? string.Empty;
    }

    private async void AddFurnitureType_OnClick(object sender, RoutedEventArgs e) => await UpdateFurnitureVocabularyAsync(vm => vm.AddFurnitureType(FurnitureTypeName.Text), "Type de meuble ajouté.");

    private async void RenameFurnitureType_OnClick(object sender, RoutedEventArgs e)
    {
        if (FurnitureTypeList.SelectedItem is not string selected) { FurnitureVocabularyStatus.Text = "Sélectionnez un type à renommer."; return; }
        await UpdateFurnitureVocabularyAsync(vm => vm.RenameFurnitureType(selected, FurnitureTypeName.Text), "Type de meuble renommé sur les fiches concernées.");
    }

    private async void DeleteFurnitureType_OnClick(object sender, RoutedEventArgs e)
    {
        if (FurnitureTypeList.SelectedItem is not string selected) { FurnitureVocabularyStatus.Text = "Sélectionnez un type à supprimer."; return; }
        await UpdateFurnitureVocabularyAsync(vm => vm.RemoveFurnitureType(selected), "Type de meuble supprimé.");
    }

    private async void AddFurnitureUsage_OnClick(object sender, RoutedEventArgs e) => await UpdateFurnitureVocabularyAsync(vm => vm.AddFurnitureUsage(FurnitureUsageName.Text), "Usage spécifique ajouté.");

    private async void RenameFurnitureUsage_OnClick(object sender, RoutedEventArgs e)
    {
        if (FurnitureUsageList.SelectedItem is not string selected) { FurnitureVocabularyStatus.Text = "Sélectionnez un usage à renommer."; return; }
        await UpdateFurnitureVocabularyAsync(vm => vm.RenameFurnitureUsage(selected, FurnitureUsageName.Text), "Usage spécifique renommé sur les fiches concernées.");
    }

    private async void DeleteFurnitureUsage_OnClick(object sender, RoutedEventArgs e)
    {
        if (FurnitureUsageList.SelectedItem is not string selected) { FurnitureVocabularyStatus.Text = "Sélectionnez un usage à supprimer."; return; }
        await UpdateFurnitureVocabularyAsync(vm => vm.RemoveFurnitureUsage(selected), "Usage spécifique supprimé.");
    }

    private async Task UpdateFurnitureVocabularyAsync(Action<MainViewModel> mutation, string successMessage)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        try
        {
            mutation(vm);
            if (!await vm.SaveCatalogAsync()) return;
            vm.RefreshFurnitureVocabularies();
            FurnitureVocabularyStatus.Text = successMessage;
        }
        catch (Exception exception)
        {
            FurnitureVocabularyStatus.Text = exception.Message;
        }
    }

    private void UniverseList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UniverseList.SelectedItem is not CatalogUniverseRecord item || DataContext is not MainViewModel vm)
        {
            UniverseImagePreview.Source = null;
            return;
        }
        var path = string.IsNullOrWhiteSpace(item.ImageRelativePath) ? string.Empty : Path.Combine(vm.SharedRoot, item.ImageRelativePath);
        UniverseImagePreview.Source = File.Exists(path) ? LoadPreview(path) : null;
    }

    private static BitmapImage LoadPreview(string path)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 720; image.UriSource = new Uri(path, UriKind.Absolute); image.EndInit(); image.Freeze();
        return image;
    }

}
