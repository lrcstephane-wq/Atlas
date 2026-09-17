using System.Windows;
using System.Windows.Controls;

namespace Atlas.App.Views;

public partial class HorizonShell : UserControl
{
    public HorizonShell() => InitializeComponent();

    public void UpdateWindowState(bool maximized)
    {
        MaximizeButton.Content = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Restaurer" : "Agrandir";
    }

    private Window? HostWindow => Window.GetWindow(this);

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is { } window) SystemCommands.MinimizeWindow(window);
    }

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is not { } window) return;
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is { } window) SystemCommands.CloseWindow(window);
    }
}
