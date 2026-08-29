using System.Windows;
using System.Windows.Input;

namespace Atlas.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel;
        Loaded += async (_, _) => await _viewModel.CheckAutoUpdateAsync();
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
