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
    private readonly ObservableCollection<ComponentTagRecord> _tags = [];
    private readonly ObservableCollection<string> _universes = [];
    private ComponentTaxonomy _taxonomy = new();
    private Button? _activeSettingsButton;

    public SettingsView()
    {
        InitializeComponent();
        TagList.ItemsSource = _tags;
        UniverseList.ItemsSource = _universes;
        CollectionViewSource.GetDefaultView(_tags).Filter = FilterTag;
    }

    private async void SettingsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_tags.Count > 0) return;
        await ReloadTaxonomyAsync();
        if (DataContext is MainViewModel vm) foreach (var universe in vm.CatalogUniverses.Order(StringComparer.CurrentCultureIgnoreCase)) _universes.Add(universe);
        ActivateSettingsButton(GeneralSettingsButton);
    }

    private async Task ReloadTaxonomyAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _taxonomy = await ComponentTaxonomyStore.LoadAsync(vm.SharedRoot);
        RefreshCollections();
        TaxonomyStatus.Text = $"{_tags.Count} tag(s) disponible(s).";
    }

    private void RefreshCollections()
    {
        var selectedTagId = (TagList.SelectedItem as ComponentTagRecord)?.Id;
        _tags.Clear();
        foreach (var tag in _taxonomy.Tags.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)) _tags.Add(tag);
        TagList.SelectedItem = _tags.FirstOrDefault(x => x.Id == selectedTagId) ?? _tags.FirstOrDefault();
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

    private void TagSearch_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) CollectionViewSource.GetDefaultView(_tags).Refresh();
    }

    private bool FilterTag(object item)
    {
        if (item is not ComponentTagRecord tag) return false;
        var query = TagSearch?.Text.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(query)
            || tag.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || tag.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || tag.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void AddTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true }) return;
        var label = NewTagName.Text.Trim();
        if (string.IsNullOrWhiteSpace(label)) { TaxonomyStatus.Text = "Saisissez le libellé du nouveau tag."; return; }
        if (_tags.Any(x => x.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) { TaxonomyStatus.Text = "Ce tag existe déjà."; return; }
        var tag = new ComponentTagRecord { Label = label, Category = "Autre" };
        _taxonomy.Tags.Add(tag); NewTagName.Clear(); RefreshCollections(); TagList.SelectedItem = tag;
        TaxonomyStatus.Text = "Tag créé. Complétez sa fiche puis enregistrez.";
    }

    private void DeleteTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm || TagList.SelectedItem is not ComponentTagRecord tag) return;
        var affected = vm.Components.Count(x => x.AddedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase));
        if (!AtlasDialog.Confirm($"Supprimer le tag « {tag.Label} » ?", "Suppression du tag", $"{affected} composant(s) utilisent actuellement ce tag. Il disparaîtra de leurs filtres.")) return;
        _taxonomy.Tags.Remove(tag);
        foreach (var family in _taxonomy.Families)
        {
            RemoveIgnoreCase(family.TagIds, tag.Id);
            foreach (var type in family.Types)
            {
                RemoveIgnoreCase(type.TagIds, tag.Id);
            }
        }
        RefreshCollections();
        TaxonomyStatus.Text = "Tag supprimé. Enregistrez pour confirmer.";
    }

    private async void SaveTaxonomy_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanEdit: true } vm) return;
        var duplicateTags = _tags.Where(x => !string.IsNullOrWhiteSpace(x.Label)).GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicateTags is not null) { TaxonomyStatus.Text = $"Tag en double : {duplicateTags.Key}"; return; }
        if (_tags.Any(x => string.IsNullOrWhiteSpace(x.Label))) { TaxonomyStatus.Text = "Chaque tag doit avoir un libellé."; return; }

        foreach (var tag in _tags) { tag.Label = tag.Label.Trim(); tag.Description = tag.Description.Trim(); }
        await ComponentTaxonomyStore.SaveAsync(vm.SharedRoot, _taxonomy);
        await vm.ReloadTaxonomyAsync();
        TaxonomyStatus.Text = $"Référentiel enregistré · {_tags.Count} tag(s).";
        vm.StatusText = "Tags partagés enregistrés.";
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

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }
}
