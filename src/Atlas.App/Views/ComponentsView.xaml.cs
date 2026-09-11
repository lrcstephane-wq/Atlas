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
    private readonly ObservableCollection<CapabilityChoiceViewModel> _capabilityChoices = [];
    private List<CapabilityTagRecord> _tags = [];
    private bool _isRefreshing;

    public ComponentsView()
    {
        InitializeComponent();
        CapabilityList.ItemsSource = _capabilityChoices;
    }

    private async void ComponentsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadCapabilitiesAsync();
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private async Task ReloadCapabilitiesAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _tags = await CapabilityTagStore.LoadAsync(vm.SharedRoot);
    }

    private void ComponentSelection_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshFamilyOptions();
        RefreshEditor();
    }

    private void RefreshFamilyOptions()
    {
        if (DataContext is not MainViewModel vm || AtlasFamilyCombo is null) return;
        var values = vm.Components
            .Select(component => component.FamilyName)
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
            component.NormalizeCapabilities();
            DetectedFamilyText.Text = string.IsNullOrWhiteSpace(component.FamilyName) ? "Non détectée" : component.FamilyName;
            AtlasFamilyCombo.Text = component.EffectiveFamilyName;
            FamilyOverrideHint.Visibility = component.IsFamilyOverridden ? Visibility.Visible : Visibility.Collapsed;

            _capabilityChoices.Clear();
            foreach (var tag in _tags.Where(x => x.IsActive).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase))
            {
                var inherited = tag.DefaultFamilyNames.Any(family => family.Equals(component.EffectiveFamilyName, StringComparison.OrdinalIgnoreCase));
                var removed = component.RemovedInheritedCapabilityIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
                var added = component.AddedCapabilityIds.Contains(tag.Id, StringComparer.OrdinalIgnoreCase);
                var selected = (inherited && !removed) || added;
                _capabilityChoices.Add(new CapabilityChoiceViewModel(tag, inherited, selected, CapabilityChoice_OnChanged));
            }
            NoCapabilitiesText.Visibility = _capabilityChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CapabilityTagStore.RebuildLegacyCapabilities(component, _tags);
        }
        finally { _isRefreshing = false; }
    }

    private void CapabilityChoice_OnChanged(CapabilityChoiceViewModel choice)
    {
        if (_isRefreshing || DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        component.NormalizeCapabilities();

        RemoveIgnoreCase(component.AddedCapabilityIds, choice.Id);
        RemoveIgnoreCase(component.RemovedInheritedCapabilityIds, choice.Id);

        if (choice.IsInherited)
        {
            if (!choice.IsSelected) component.RemovedInheritedCapabilityIds.Add(choice.Id);
        }
        else if (choice.IsSelected)
        {
            component.AddedCapabilityIds.Add(choice.Id);
        }

        CapabilityTagStore.RebuildLegacyCapabilities(component, _tags);
        vm.StatusText = "Capacités du composant modifiées. Pensez à enregistrer.";
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
        CapabilityTagStore.RebuildLegacyCapabilities(component, _tags);
        RefreshEditor();
    }

    private void ResetFamily_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedComponent is not { } component) return;
        component.UseDetectedFamily();
        CapabilityTagStore.RebuildLegacyCapabilities(component, _tags);
        RefreshEditor();
        vm.StatusText = "Famille Atlas réalignée sur la famille Biblidéo.";
    }

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }
}
