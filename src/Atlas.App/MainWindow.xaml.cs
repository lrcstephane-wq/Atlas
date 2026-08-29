using System.Windows;

namespace Atlas.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += async (_, _) => await _viewModel.CheckAutoUpdateAsync();
    }
}
