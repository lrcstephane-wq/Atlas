using System.Linq;
using System.Windows.Controls;
using System.Windows;
using Atlas.App.ViewModels;

namespace Atlas.App.Views;

public partial class FurnitureView : UserControl
{
    public FurnitureView() => InitializeComponent();

    private void OpenFurnitureCatalog_OnClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window) window.OpenFurnitureCatalog();
    }

    private void CompositionGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is DataGrid grid)
            vm.SetSelectedCompositionLines(grid.SelectedItems.Cast<FurnitureCompositionLineViewModel>());
    }
}
