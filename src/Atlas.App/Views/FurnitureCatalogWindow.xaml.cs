using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Atlas.App.Services;
using Atlas.App.ViewModels;

namespace Atlas.App.Views;

public partial class FurnitureCatalogWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly FurnitureCatalogViewModel _catalogViewModel;

    public FurnitureCatalogWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        DataContext = _catalogViewModel = new FurnitureCatalogViewModel(mainViewModel);
        RestoreWindowState();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

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
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(8);
        WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    private void ClearFilters_OnClick(object sender, RoutedEventArgs e)
    {
        _catalogViewModel.ClearFilters();
        SearchBox.Focus();
    }

    private async void OpenSelected_OnClick(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async void FurnitureGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) await OpenSelectedAsync();
    }

    private async Task OpenSelectedAsync()
    {
        if (!await _mainViewModel.TryOpenFurnitureAsync(_catalogViewModel.SelectedFurniture)) return;
        _catalogViewModel.Refresh();
        if (Application.Current.MainWindow is { } mainWindow)
        {
            if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }
    }

    private async void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !SearchBox.IsKeyboardFocusWithin)
        {
            await OpenSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void RestoreWindowState()
    {
        var state = FurnitureCatalogWindowStateStore.Load();
        if (state is null || state.Width < MinWidth || state.Height < MinHeight || !IntersectsVirtualScreen(state)) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = state.Left;
        Top = state.Top;
        Width = state.Width;
        Height = state.Height;
        if (state.IsMaximized) Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    private static bool IntersectsVirtualScreen(FurnitureCatalogWindowState state)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var window = new Rect(state.Left, state.Top, state.Width, state.Height);
        var intersection = Rect.Intersect(screen, window);
        return !intersection.IsEmpty && intersection.Width >= 120 && intersection.Height >= 80;
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        FurnitureCatalogWindowStateStore.Save(new FurnitureCatalogWindowState
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = WindowState == WindowState.Maximized
        });
        _catalogViewModel.Dispose();
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
