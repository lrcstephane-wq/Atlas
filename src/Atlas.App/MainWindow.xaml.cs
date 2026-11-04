using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using System.ComponentModel;
using System.Windows.Threading;

namespace Atlas.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private FurnitureCatalogWindow? _furnitureCatalogWindow;
    private readonly DispatcherTimer _adminLockTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DateTimeOffset _sessionStarted = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private bool _lockingAdminSession;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
        Loaded += async (_, _) => await _viewModel.CheckAutoUpdateAsync();
        PreviewMouseDown += (_, _) => _lastActivity = DateTimeOffset.UtcNow;
        PreviewTouchDown += (_, _) => _lastActivity = DateTimeOffset.UtcNow;
        _adminLockTimer.Tick += AdminLockTimer_OnTick;
        if (_viewModel.IsAdministrator) _adminLockTimer.Start();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    public void OpenFurnitureCatalog()
    {
        if (_furnitureCatalogWindow is { IsLoaded: true })
        {
            if (_furnitureCatalogWindow.WindowState == WindowState.Minimized) _furnitureCatalogWindow.WindowState = WindowState.Normal;
            _furnitureCatalogWindow.Activate();
            return;
        }

        _furnitureCatalogWindow = new FurnitureCatalogWindow(_viewModel);
        _furnitureCatalogWindow.Closed += (_, _) => _furnitureCatalogWindow = null;
        _furnitureCatalogWindow.Show();
    }
    private async void AccessAdministrator_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).OpenAdministratorSessionAsync(this);
    private async void Logout_Click(object sender, RoutedEventArgs e) => await ((App)Application.Current).LogoutAdministratorAsync(this);
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

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _lastActivity = DateTimeOffset.UtcNow;
        if (e.Key != Key.K || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !_viewModel.CanUseAdministrativeInterfaces) return;
        OpenFurnitureCatalog();
        e.Handled = true;
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        _adminLockTimer.Stop();
        _furnitureCatalogWindow?.Close();
        _furnitureCatalogWindow = null;
    }

    private async void AdminLockTimer_OnTick(object? sender, EventArgs e)
    {
        if (_lockingAdminSession || !_viewModel.IsAdministrator) return;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastActivity < TimeSpan.FromMinutes(15) && now - _sessionStarted < TimeSpan.FromHours(2)) return;
        _lockingAdminSession = true;
        _adminLockTimer.Stop();
        AtlasDialog.Info("La session Administrateur Idéo a été fermée automatiquement.", "Session Idéo verrouillée");
        await ((App)Application.Current).LogoutAdministratorAsync(this);
    }

    private void ClientFacetFavorite_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFacetViewModel facet }) return;
        _viewModel.ToggleClientFavorite(facet);
        e.Handled = true;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, 2);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var work = monitorInfo.WorkArea;
                var screen = monitorInfo.MonitorArea;
                info.MaxPosition.X = Math.Abs(work.Left - screen.Left);
                info.MaxPosition.Y = Math.Abs(work.Top - screen.Top);
                info.MaxSize.X = Math.Abs(work.Right - work.Left);
                info.MaxSize.Y = Math.Abs(work.Bottom - work.Top);
            }
        }
        Marshal.StructureToPtr(info, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved; public Point MaxSize; public Point MaxPosition; public Point MinTrackSize; public Point MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public NativeRect MonitorArea; public NativeRect WorkArea; public uint Flags; }
}
