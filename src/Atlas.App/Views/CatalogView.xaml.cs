using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using Atlas.App.ViewModels;

namespace Atlas.App.Views;

public partial class CatalogView : UserControl
{
    private const double MinimumDockedFilterWidth = 1180;
    private Point _dragStart;

    public CatalogView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateFilterDockingMode();
        SizeChanged += (_, _) => UpdateFilterDockingMode();
        DataContextChanged += (_, _) => UpdateFilterDockingMode();
    }

    private void UpdateFilterDockingMode()
    {
        if (DataContext is MainViewModel vm)
            vm.SetClientFilterDockingAvailable(ActualWidth >= MinimumDockedFilterWidth);
    }

    private void FurnitureCard_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FurnitureCardViewModel card } || DataContext is not MainViewModel vm) return;
        vm.SelectedClientFurnitureCard = card;
    }

    private void FurnitureCard_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FurnitureCardViewModel card } || DataContext is not MainViewModel vm || !vm.HasFullHorizonAccess) return;
        vm.SelectedClientFurnitureCard = card;
        card.IsChosen = !card.IsChosen;
        e.Handled = true;
    }

    private void FurnitureChoice_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: FurnitureCardViewModel card } checkBox || DataContext is not MainViewModel vm) return;
        vm.SelectedClientFurnitureCard = card;
        if (!vm.HasFullHorizonAccess) checkBox.IsChecked = false;
        e.Handled = true;
    }

    private void ClientFacetFavorite_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFacetViewModel facet } || DataContext is not MainViewModel vm) return;
        vm.ToggleClientFavorite(facet);
        e.Handled = true;
    }

    private void OpenMoreFilters_OnClick(object sender, RoutedEventArgs e)
    {
        MoreFiltersPopup.IsOpen = !MoreFiltersPopup.IsOpen;
    }

    private void ClientSearchSuggestion_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string suggestion } || DataContext is not MainViewModel vm) return;
        vm.ApplyClientSearchSuggestion(suggestion);
        ClientSearchBox.Focus();
        ClientSearchBox.CaretIndex = ClientSearchBox.Text.Length;
        e.Handled = true;
    }

    private void TopSolidDragCapsule_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void TopSolidDragCapsule_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || DataContext is not MainViewModel vm || !vm.HasFullHorizonAccess || !vm.IsTopSolidBridgeReady) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var files = vm.TopSolidBridgeFiles.Where(File.Exists).ToArray();
        if (files.Length == 0)
        {
            AtlasDialog.Warning("Les copies préparées sont introuvables. Ouvrez à nouveau la passerelle.", "Passerelle TopSolid");
            return;
        }

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, files);
        var effect = DragDrop.DoDragDrop(TopSolidDragCapsule, data, DragDropEffects.Copy);
        if ((effect & DragDropEffects.Copy) == DragDropEffects.Copy) vm.CompleteTopSolidBridgeDrop();
    }
}
