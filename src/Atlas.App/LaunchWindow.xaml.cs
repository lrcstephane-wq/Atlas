using System.Windows;

namespace Atlas.App;

public enum AtlasEntryMode
{
    None,
    SiteManager,
    IdeoAdministrator,
    Creator,
    User
}

public partial class LaunchWindow : Window
{
    public AtlasEntryMode SelectedMode { get; private set; }

    public LaunchWindow() => InitializeComponent();

    private void SiteManager_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = AtlasEntryMode.SiteManager;
        DialogResult = true;
    }

    private void IdeoAdministrator_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = AtlasEntryMode.IdeoAdministrator;
        DialogResult = true;
    }

    private void User_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = AtlasEntryMode.User;
        DialogResult = true;
    }

    private void Creator_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = AtlasEntryMode.Creator;
        DialogResult = true;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
