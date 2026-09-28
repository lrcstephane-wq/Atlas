using System.Windows;

namespace Atlas.App;

public enum AtlasEntryMode
{
    None,
    Administrator,
    Creator,
    User
}

public partial class LaunchWindow : Window
{
    public AtlasEntryMode SelectedMode { get; private set; }

    public LaunchWindow() => InitializeComponent();

    private void Administrator_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = AtlasEntryMode.Administrator;
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
