using System.Windows;
using System.Windows.Controls;

namespace Atlas.App.Views;

public partial class CustomerFurnitureView : UserControl
{
    public CustomerFurnitureView() => InitializeComponent();

    private void OpenPersonalCatalog_OnClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow) mainWindow.OpenCustomerFurnitureCatalog();
    }
}
