using System.Windows;
using System.Windows.Input;
using Atlas.App.ViewModels;

namespace Atlas.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel;
        Loaded += async (_, _) => await _viewModel.CheckAutoUpdateAsync();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null || WindowFrame is null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Restaurer" : "Agrandir";
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(12);
        WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    private void ClientFacetFavorite_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFacetViewModel facet }) return;
        _viewModel.ToggleClientFavorite(facet);
        e.Handled = true;
    }
}
