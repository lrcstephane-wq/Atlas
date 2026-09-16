using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Atlas.App.ViewModels;
using Atlas.Core.Models;
using Atlas.Core.Services;

namespace Atlas.App.Views;

public partial class ComponentsView : UserControl
{
    private readonly ObservableCollection<TagChoiceViewModel> _singleTagChoices = [];
    private readonly ObservableCollection<TagChoiceViewModel> _bulkTagChoices = [];
    private ComponentTaxonomy _taxonomy = new();
    private bool _isRefreshing;
    private bool _taxonomyEventsConnected;

    public ComponentsView()
    {
        InitializeComponent();
        SingleTagList.ItemsSource = _singleTagChoices;
        BulkTagList.ItemsSource = _bulkTagChoices;
        CollectionViewSource.GetDefaultView(_singleTagChoices).Filter = FilterSingleTag;
        CollectionViewSource.GetDefaultView(_bulkTagChoices).Filter = FilterBulkTag;
    }

    private async void ComponentsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        ConnectTaxonomyEvents();
        await ReloadTaxonomyAsync();
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private async void ComponentsView_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || !IsLoaded) return;
        ConnectTaxonomyEvents();
        await ReloadTaxonomyAsync();
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private void ConnectTaxonomyEvents()
    {
        if (_taxonomyEventsConnected || DataContext is not MainViewModel vm) return;
        vm.TaxonomyChanged += MainViewModel_OnTaxonomyChanged;
        vm.PropertyChanged += MainViewModel_OnPropertyChanged;
        _taxonomyEventsConnected = true;
    }

    private void MainViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MarkedComponentCount)) RefreshBulkTagSummary();
    }

    private async void MainViewModel_OnTaxonomyChanged(object? sender, EventArgs e)
    {
        await ReloadTaxonomyAsync();
        RefreshEditor();
    }

    private async Task ReloadTaxonomyAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _taxonomy = await ComponentTaxonomyStore.LoadAsync(vm.SharedRoot);
        ComponentTaxonomyStore.SyncDetectedFamilies(_taxonomy, vm.Components);
        RefreshBulkTagChoices();
    }

    private async void ReloadTags_OnClick(object sender, RoutedEventArgs e)
    {
        await ReloadTaxonomyAsync();
        RefreshEditor();
        if (DataContext is MainViewModel vm) vm.StatusText = $"{ActiveTags().Count} tag(s) rechargé(s) depuis les paramètres.";
    }

    private void ComponentSelection_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private void RefreshFamilyOptions()
    {
        if (DataContext is not MainViewModel vm || AtlasFamilyCombo is null) return;
        var library = vm.SelectedComponent?.LibraryName;
        AtlasFamilyCombo.ItemsSource = _taxonomy.Families
            .Where(family => family.IsActive && (string.IsNullOrWhiteSpace(library) || family.LibraryName.Equals(library, StringComparison.OrdinalIgnoreCase)))
            .Select(family => family.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void RefreshEditor()
    {
        if (DataContext is not MainViewModel vm || AtlasFamilyCombo is null) return;
        _isRefreshing = true;
        try
        {
            _singleTagChoices.Clear();
            if (vm.SelectedComponent is not { } component)
            {
                DetectedFamilyText.Text = "Aucun composant sélectionné";
                SingleTagSummary.Text = "Sélectionnez un composant dans la bibliothèque.";
                NoTagsText.Visibility = Visibility.Collapsed;
                return;
            }

            component.NormalizeTags();
            DetectedFamilyText.Text = string.IsNullOrWhiteSpace(component.FamilyName) ? "Non détectée" : component.FamilyName;
            AtlasFamilyCombo.Text = component.EffectiveFamilyName;
            FamilyOverrideHint.Visibility = component.IsFamilyOverridden ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tag in ActiveTags().OrderByDescending(tag => component.AddedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase)).ThenBy(tag => tag.Label, StringComparer.CurrentCultureIgnoreCase))
            {
                var selected = component.AddedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
                _singleTagChoices.Add(new TagChoiceViewModel(tag, string.IsNullOrWhiteSpace(tag.Category) ? "Autre" : tag.Category, false, selected, string.Empty, TagChoice_OnChanged));
            }

            NoTagsText.Visibility = _singleTagChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshSingleTagSummary();
            CollectionViewSource.GetDefaultView(_singleTagChoices).Refresh();
        }
        finally { _isRefreshing = false; }
    }

    private void RefreshBulkTagChoices()
    {
        var selectedIds = _bulkTagChoices.Where(choice => choice.IsSelected).Select(choice => choice.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _isRefreshing = true;
        try
        {
            _bulkTagChoices.Clear();
            foreach (var tag in ActiveTags().OrderBy(tag => tag.Label, StringComparer.CurrentCultureIgnoreCase))
                _bulkTagChoices.Add(new TagChoiceViewModel(tag, string.IsNullOrWhiteSpace(tag.Category) ? "Autre" : tag.Category, false, selectedIds.Contains(tag.Id), string.Empty, BulkTagChoice_OnChanged));
            CollectionViewSource.GetDefaultView(_bulkTagChoices).Refresh();
            RefreshBulkTagSummary();
        }
        finally { _isRefreshing = false; }
    }

    private List<ComponentTagRecord> ActiveTags() => _taxonomy.Tags.Where(tag => tag.IsActive).ToList();

    private void TagChoice_OnChanged(TagChoiceViewModel choice)
    {
        if (_isRefreshing || DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        component.NormalizeTags();
        RemoveIgnoreCase(component.AddedTagIds, choice.Id);
        RemoveIgnoreCase(component.RemovedInheritedTagIds, choice.Id);
        component.RemovedInheritedTagReasons.Remove(choice.Id);
        if (choice.IsSelected) component.AddedTagIds.Add(choice.Id);
        vm.StatusText = "Tags du composant modifiés. Cliquez sur Enregistrer pour les partager.";
        RefreshSingleTagSummary();
    }

    private void BulkTagChoice_OnChanged(TagChoiceViewModel choice)
    {
        if (!_isRefreshing) RefreshBulkTagSummary();
    }

    private void RefreshSingleTagSummary()
    {
        var selected = _singleTagChoices.Count(choice => choice.IsSelected);
        SingleTagSummary.Text = $"{selected} tag(s) associé(s) · {_singleTagChoices.Count} disponible(s)";
    }

    private void RefreshBulkTagSummary()
    {
        var selected = _bulkTagChoices.Count(choice => choice.IsSelected);
        var components = DataContext is MainViewModel vm ? vm.MarkedComponentCount : 0;
        BulkTagSummary.Text = selected == 0 ? "Sélectionnez au moins un tag." : $"{selected} tag(s) choisi(s) pour {components} composant(s).";
        var enabled = selected > 0 && components > 0 && DataContext is MainViewModel { CanEdit: true };
        BulkAddButton.IsEnabled = enabled;
        BulkRemoveButton.IsEnabled = enabled;
    }

    private void SingleTagSearch_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) CollectionViewSource.GetDefaultView(_singleTagChoices).Refresh();
    }

    private void BulkTagSearch_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) CollectionViewSource.GetDefaultView(_bulkTagChoices).Refresh();
    }

    private bool FilterSingleTag(object item) => FilterTag(item, SingleTagSearch?.Text);
    private bool FilterBulkTag(object item) => FilterTag(item, BulkTagSearch?.Text);

    private static bool FilterTag(object item, string? search)
    {
        if (item is not TagChoiceViewModel choice) return false;
        var query = search?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(query)
            || choice.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || choice.OriginLabel.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenBulkTags_OnClick(object sender, RoutedEventArgs e) => ShowBulkMode();
    private void ShowBulkMode_OnClick(object sender, RoutedEventArgs e) => ShowBulkMode();
    private void ShowSingleMode_OnClick(object sender, RoutedEventArgs e) => ShowSingleMode();

    private void ShowBulkMode()
    {
        if (DataContext is not MainViewModel vm || vm.MarkedComponentCount == 0)
        {
            AtlasDialog.Warning("Cochez d’abord un ou plusieurs composants dans la bibliothèque.", "Affectation en masse");
            return;
        }
        SingleEditorPanel.Visibility = Visibility.Collapsed;
        BulkEditorPanel.Visibility = Visibility.Visible;
        ValidateSingleComponentButton.Visibility = Visibility.Collapsed;
        SetModeButtons(false);
        RefreshBulkTagSummary();
    }

    private void ShowSingleMode()
    {
        BulkEditorPanel.Visibility = Visibility.Collapsed;
        SingleEditorPanel.Visibility = Visibility.Visible;
        ValidateSingleComponentButton.Visibility = Visibility.Visible;
        SetModeButtons(true);
    }

    private void SetModeButtons(bool single)
    {
        var active = new SolidColorBrush(Color.FromRgb(57, 127, 246));
        var inactive = new SolidColorBrush(Color.FromRgb(28, 43, 66));
        SingleModeButton.Background = single ? active : inactive;
        BulkModeButton.Background = single ? inactive : active;
    }

    private async void BulkAddTags_OnClick(object sender, RoutedEventArgs e) => await ApplyBulkTagsAsync(true);
    private async void BulkRemoveTags_OnClick(object sender, RoutedEventArgs e) => await ApplyBulkTagsAsync(false);

    private async Task ApplyBulkTagsAsync(bool add)
    {
        if (DataContext is not MainViewModel vm) return;
        var cards = vm.ComponentCards.Where(card => card.IsMarked).ToArray();
        var tags = _bulkTagChoices.Where(choice => choice.IsSelected).Select(choice => choice.Tag).ToArray();
        if (cards.Length == 0 || tags.Length == 0) return;

        var verb = add ? "Ajouter" : "Retirer";
        if (!AtlasDialog.Confirm($"{verb} {tags.Length} tag(s) sur {cards.Length} composant(s) ?", "Affectation en masse", "L’opération sera enregistrée immédiatement dans le catalogue partagé.")) return;

        var snapshots = cards.ToDictionary(card => card.Id, card => card.Record.AddedTagIds.ToList(), StringComparer.OrdinalIgnoreCase);
        ComponentTaxonomyStore.ApplyDirectTags(cards.Select(card => card.Record), tags.Select(tag => tag.Id), add);

        if (!await vm.SaveCatalogAsync())
        {
            foreach (var card in cards)
            {
                card.Record.AddedTagIds.Clear();
                card.Record.AddedTagIds.AddRange(snapshots[card.Id]);
            }
            RefreshEditor();
            return;
        }

        vm.StatusText = $"{tags.Length} tag(s) {(add ? "ajouté(s) à" : "retiré(s) de")} {cards.Length} composant(s).";
        AtlasDialog.Info(vm.StatusText, "Affectation terminée");
        RefreshEditor();
    }

    private void AtlasFamilyCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        ApplyFamily(component, AtlasFamilyCombo.SelectedItem?.ToString() ?? AtlasFamilyCombo.Text);
        vm.StatusText = "Famille Atlas modifiée. Pensez à enregistrer.";
    }

    private void AtlasFamilyCombo_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isRefreshing || DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        ApplyFamily(component, AtlasFamilyCombo.Text);
        vm.StatusText = "Famille Atlas modifiée. Pensez à enregistrer.";
    }

    private void ApplyFamily(ComponentRecord component, string? requested)
    {
        var value = requested?.Trim() ?? string.Empty;
        component.AtlasFamilyNameOverride = string.IsNullOrWhiteSpace(value) || value.Equals(component.FamilyName, StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
        RefreshEditor();
    }

    private void ResetFamily_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        component.UseDetectedFamily();
        RefreshEditor();
        vm.StatusText = "Famille Atlas réalignée sur la famille Biblidéo.";
    }

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }
}
