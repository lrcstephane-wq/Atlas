using System.Linq;
using System.Windows.Controls;
using Atlas.App.ViewModels;

namespace Atlas.App.Views;

public partial class FurnitureView : UserControl
{
    public FurnitureView() => InitializeComponent();

    private void CompositionGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is DataGrid grid)
            vm.SetSelectedCompositionLines(grid.SelectedItems.Cast<FurnitureCompositionLineViewModel>());
    }
}
