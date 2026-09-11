using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Atlas.Core.Models;
using Atlas.Core.Services;

namespace Atlas.App.Views;

public partial class SettingsView : UserControl
{
    private readonly ObservableCollection<CapabilityTagRecord> _capabilityTags = [];

    public SettingsView()
    {
        InitializeComponent();
        CapabilityGrid.ItemsSource = _capabilityTags;
    }

    private async void SettingsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadCapabilitiesAsync();
    }

    private async Task ReloadCapabilitiesAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        _capabilityTags.Clear();
        foreach (var tag in await CapabilityTagStore.LoadAsync(vm.SharedRoot)) _capabilityTags.Add(tag);
        CapabilityStatus.Text = _capabilityTags.Count == 0
            ? "Aucune capacité créée pour le moment. Ajoutez vos premières capacités ici."
            : $"{_capabilityTags.Count} capacité(s) dans le référentiel partagé.";
    }

    private void AddCapability_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanEdit) return;
        var tag = new CapabilityTagRecord { Label = "Nouvelle capacité", IsActive = true };
        _capabilityTags.Add(tag);
        CapabilityGrid.SelectedItem = tag;
        CapabilityGrid.ScrollIntoView(tag);
        CapabilityStatus.Text = "Nouvelle capacité ajoutée. Renseignez son libellé et ses familles par défaut.";
    }

    private void DeleteCapability_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanEdit || CapabilityGrid.SelectedItem is not CapabilityTagRecord selected) return;
        _capabilityTags.Remove(selected);
        foreach (var component in vm.Components)
        {
            RemoveIgnoreCase(component.AddedCapabilityIds, selected.Id);
            RemoveIgnoreCase(component.RemovedInheritedCapabilityIds, selected.Id);
            CapabilityTagStore.RebuildLegacyCapabilities(component, _capabilityTags);
        }
        CapabilityStatus.Text = "Capacité supprimée du référentiel local. Enregistrez pour confirmer.";
    }

    private async void SaveCapabilities_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanEdit) return;
        try
        {
            var duplicates = _capabilityTags
                .Where(x => !string.IsNullOrWhiteSpace(x.Label))
                .GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                CapabilityStatus.Text = $"Libellé en double : {string.Join(", ", duplicates)}";
                return;
            }

            foreach (var tag in _capabilityTags)
            {
                tag.Label = tag.Label.Trim();
                if (string.IsNullOrWhiteSpace(tag.Label))
                {
                    CapabilityStatus.Text = "Chaque capacité doit avoir un libellé.";
                    return;
                }
            }

            await CapabilityTagStore.SaveAsync(vm.SharedRoot, _capabilityTags);
            foreach (var component in vm.Components) CapabilityTagStore.RebuildLegacyCapabilities(component, _capabilityTags);
            vm.StatusText = "Référentiel des capacités enregistré. Pensez aussi à enregistrer le catalogue si des composants ont été recalculés.";
            CapabilityStatus.Text = $"Référentiel enregistré · {_capabilityTags.Count} capacité(s).";
        }
        catch (Exception exception)
        {
            CapabilityStatus.Text = exception.Message;
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

    private static void RemoveIgnoreCase(List<string> values, string id)
    {
        var existing = values.FirstOrDefault(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
    }
}
