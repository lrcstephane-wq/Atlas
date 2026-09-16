using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using Atlas.App.ViewModels;

namespace Atlas.App.Views;

public partial class CatalogView : UserControl
{
    private Point _dragStart;

    public CatalogView() => InitializeComponent();

    private void FurnitureCard_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FurnitureCardViewModel card } || DataContext is not MainViewModel vm) return;
        vm.SelectedClientFurnitureCard = card;
    }

    private void TopSolidDragCapsule_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void TopSolidDragCapsule_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || DataContext is not MainViewModel vm || !vm.IsTopSolidBridgeReady) return;
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
