using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.Models;

namespace Atlas.App.Views;

public partial class CustomerFurnitureCatalogWindow : Window
{
    private readonly MainViewModel _main;
    private readonly CustomerFurnitureCatalogViewModel _catalog;
    private readonly Dictionary<string, WeakReference<BitmapImage>> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);

    public CustomerFurnitureCatalogWindow(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        DataContext = _catalog = new CustomerFurnitureCatalogViewModel(main);
        RestoreWindowState();
        Loaded += (_, _) => { ScrollSelectedIntoView(); SearchBox.Focus(); };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void ToggleMaximize() { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void Window_StateChanged(object? sender, EventArgs e) { if (MaximizeButton is null || WindowFrame is null) return; var maximized = WindowState == WindowState.Maximized; MaximizeButton.Content = maximized ? "❐" : "□"; WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(8); WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1); }
    private void ClearFilters_OnClick(object sender, RoutedEventArgs e) { _catalog.ClearFilters(); SearchBox.Focus(); }
    private void ViewMode_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(ScrollSelectedIntoView));
    private void OpenSelected_OnClick(object sender, RoutedEventArgs e) => OpenSelected();
    private void FurnitureItems_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) OpenSelected(); }

    private void OpenSelected()
    {
        if (_catalog.SelectedFurniture is null) return;
        _main.OpenCustomerFurniture(_catalog.SelectedFurniture);
        if (Application.Current.MainWindow is { } mainWindow) { if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal; mainWindow.Activate(); }
    }

    private void ScrollSelectedIntoView() { if (_catalog.SelectedFurniture is not { } selected) return; if (_catalog.IsCardView) FurnitureCards.ScrollIntoView(selected); else FurnitureGrid.ScrollIntoView(selected); }

    private async void CardImage_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.DataContext is not CustomerFurnitureItem furniture) return;
        var path = _catalog.ImagePath(furniture);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (_thumbnailCache.TryGetValue(path, out var cached) && cached.TryGetTarget(out var thumbnail)) { image.Source = thumbnail; return; }
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (!image.IsLoaded || !ReferenceEquals(image.DataContext, furniture)) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 400; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            _thumbnailCache[path] = new WeakReference<BitmapImage>(bitmap); image.Source = bitmap;
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { } catch (NotSupportedException) { }
    }
    private void CardImage_OnUnloaded(object sender, RoutedEventArgs e) { if (sender is Image image) image.Source = null; }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Enter && !SearchBox.IsKeyboardFocusWithin) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void RestoreWindowState()
    {
        var state = FurnitureCatalogWindowStateStore.Load("personnel");
        if (state is null || state.Width < MinWidth || state.Height < MinHeight) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (Rect.Intersect(screen, new Rect(state.Left, state.Top, state.Width, state.Height)).IsEmpty) return;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = state.Left; Top = state.Top; Width = state.Width; Height = state.Height; _catalog.IsCardView = state.IsCardView;
        if (state.IsMaximized) Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        FurnitureCatalogWindowStateStore.Save(new FurnitureCatalogWindowState { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, IsMaximized = WindowState == WindowState.Maximized, IsCardView = _catalog.IsCardView }, "personnel");
        _catalog.Dispose();
    }
}
