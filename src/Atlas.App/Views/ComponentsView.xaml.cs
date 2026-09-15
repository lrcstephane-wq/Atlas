using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Atlas.App.ViewModels;
using Atlas.Core.Models;
using Atlas.Core.Services;

namespace Atlas.App.Views;

public partial class ComponentsView : UserControl
{
    private readonly ObservableCollection<TagChoiceViewModel> _inheritedTagChoices = [];
    private readonly ObservableCollection<TagChoiceViewModel> _specificTagChoices = [];
    private readonly ObservableCollection<TagChoiceViewModel> _availableTagChoices = [];
    private ComponentTaxonomy _taxonomy = new();
    private bool _isRefreshing;

    public ComponentsView()
    {
        InitializeComponent();
        InheritedTagList.ItemsSource = _inheritedTagChoices;
        SpecificTagList.ItemsSource = _specificTagChoices;
        AvailableTagList.ItemsSource = _availableTagChoices;
    }

    private async void ComponentsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadTaxonomyAsync();
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private async void ComponentsView_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || !IsLoaded) return;
        await ReloadTaxonomyAsync();
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private async Task ReloadTaxonomyAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _taxonomy = await ComponentTaxonomyStore.LoadAsync(vm.SharedRoot);
        ComponentTaxonomyStore.SyncDetectedFamilies(_taxonomy, vm.Components);
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
        var values = _taxonomy.Families
            .Where(family => family.IsActive && (string.IsNullOrWhiteSpace(library) || family.LibraryName.Equals(library, StringComparison.OrdinalIgnoreCase)))
            .Select(family => family.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        AtlasFamilyCombo.ItemsSource = values;
    }

    private void RefreshEditor()
    {
        if (DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component || AtlasFamilyCombo is null) return;
        _isRefreshing = true;
        try
        {
            component.NormalizeTags();
            DetectedFamilyText.Text = string.IsNullOrWhiteSpace(component.FamilyName) ? "Non détectée" : component.FamilyName;
            AtlasFamilyCombo.Text = component.EffectiveFamilyName;
            FamilyOverrideHint.Visibility = component.IsFamilyOverridden ? Visibility.Visible : Visibility.Collapsed;

            _inheritedTagChoices.Clear(); _specificTagChoices.Clear(); _availableTagChoices.Clear();
            var inheritedOrigins = ComponentTaxonomyStore.InheritedTagOrigins(component, _taxonomy);
            foreach (var tag in _taxonomy.Tags.Where(x => x.IsActive).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase))
            {
                var inherited = inheritedOrigins.TryGetValue(tag.Id, out var origin);
                var removed = component.RemovedInheritedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
                var added = component.AddedTagIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
                var selected = (inherited && !removed) || added;
                var reason = component.RemovedInheritedTagReasons.GetValueOrDefault(tag.Id, string.Empty);
                var choice = new TagChoiceViewModel(tag, inherited ? origin! : added ? "Ajouté sur ce composant" : tag.Category, inherited, selected, reason, TagChoice_OnChanged, TagReason_OnChanged);
                if (inherited) _inheritedTagChoices.Add(choice);
                else if (added) _specificTagChoices.Add(choice);
                else _availableTagChoices.Add(choice);
            }
            NoTagsText.Visibility = _taxonomy.Tags.Any(x => x.IsActive) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _isRefreshing = false; }
    }

    private void TagChoice_OnChanged(TagChoiceViewModel choice)
    {
        if (_isRefreshing || DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        component.NormalizeTags();

        RemoveIgnoreCase(component.AddedTagIds, choice.Id);
        RemoveIgnoreCase(component.RemovedInheritedTagIds, choice.Id);

        if (choice.IsInherited)
        {
            if (!choice.IsSelected)
            {
                component.RemovedInheritedTagIds.Add(choice.Id);
                component.RemovedInheritedTagReasons[choice.Id] = string.IsNullOrWhiteSpace(choice.ExclusionReason) ? "À préciser" : choice.ExclusionReason.Trim();
            }
            else component.RemovedInheritedTagReasons.Remove(choice.Id);
        }
        else if (choice.IsSelected)
        {
            component.AddedTagIds.Add(choice.Id);
        }

        vm.StatusText = "Tags du composant modifiés. Pensez à enregistrer.";
        Dispatcher.BeginInvoke(new Action(RefreshEditor));
    }

    private void TagReason_OnChanged(TagChoiceViewModel choice)
    {
        if (_isRefreshing || DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component || !choice.IsExcluded) return;
        component.RemovedInheritedTagReasons[choice.Id] = choice.ExclusionReason.Trim();
        vm.StatusText = "Motif d’exclusion modifié. Pensez à enregistrer.";
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
