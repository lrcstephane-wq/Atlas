using System.IO;
using System.Windows;
using System.Windows.Input;
using Atlas.Core.Models;
using Atlas.Core.Services;
using Microsoft.Win32;

namespace Atlas.App;

public partial class LoginWindow : Window
{
    private readonly LocalBootstrap _bootstrap;
    private bool _firstRun;
    public UserAccount? AuthenticatedUser { get; private set; }
    public string SharedRoot => SharedRootBox.Text.Trim();

    public LoginWindow(LocalBootstrap bootstrap)
    {
        InitializeComponent();
        _bootstrap = bootstrap;
        SharedRootBox.Text = bootstrap.SharedRoot;
        Loaded += async (_, _) => await RefreshModeAsync();
    }

    private async Task RefreshModeAsync()
    {
        if (string.IsNullOrWhiteSpace(SharedRoot))
        {
            _firstRun = true;
            TitleText.Text = "Choisir l’espace partagé";
            HelpText.Text = "Sélectionnez le dossier réseau Atlas avant de créer le premier administrateur.";
            DisplayNameLabel.Visibility = DisplayNameBox.Visibility = Visibility.Visible;
            SubmitButton.Content = "Créer et ouvrir Atlas";
            return;
        }
        try
        {
            if (!AtlasWorkspaceService.IsWorkspace(SharedRoot) && IsLegacyWorkspace(SharedRoot))
                await AtlasWorkspaceService.EnsureAsync(SharedRoot);
            var accounts = await new UserAccountStore(SharedRoot).LoadAsync();
            _firstRun = accounts.Count == 0;
            TitleText.Text = _firstRun ? "Créer l’espace Atlas" : "Connexion";
            HelpText.Text = _firstRun ? "Confirmez l’emplacement puis créez le premier compte administrateur." : "Connexion à l’espace Atlas existant.";
            DisplayNameLabel.Visibility = DisplayNameBox.Visibility = _firstRun ? Visibility.Visible : Visibility.Collapsed;
            SubmitButton.Content = _firstRun ? "Créer et ouvrir Atlas" : "Se connecter";
        }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }

    private async void SubmitButton_OnClick(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(SharedRoot)) throw new InvalidOperationException("Le dossier partagé Atlas est obligatoire.");
            if (_firstRun)
            {
                if (!AtlasWorkspaceService.IsWorkspace(SharedRoot) && Directory.Exists(SharedRoot) && Directory.EnumerateFileSystemEntries(SharedRoot).Any())
                    throw new InvalidOperationException("Ce dossier n’est pas vide et ne contient pas un espace Atlas reconnu.");
                await AtlasWorkspaceService.EnsureAsync(SharedRoot, $"Atlas · {DisplayNameBox.Text.Trim()}");
            }
            else if (!AtlasWorkspaceService.IsWorkspace(SharedRoot))
                throw new InvalidDataException("Le dossier choisi n’est pas un espace Atlas reconnu.");
            _bootstrap.SharedRoot = SharedRoot;
            await SharedCatalogStore.SaveBootstrapAsync(_bootstrap);
            var userStore = new UserAccountStore(SharedRoot);
            AuthenticatedUser = _firstRun
                ? await userStore.CreateFirstAdministratorAsync(LoginBox.Text, DisplayNameBox.Text, PasswordBox.Password)
                : await userStore.AuthenticateAsync(LoginBox.Text, PasswordBox.Password);
            if (AuthenticatedUser is null)
            {
                ErrorText.Text = "Identifiant ou mot de passe incorrect.";
                return;
            }
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
        finally { SubmitButton.IsEnabled = true; }
    }

    private void PasswordBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SubmitButton_OnClick(sender, new RoutedEventArgs());
    }

    private async void BrowseRoot_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choisir le dossier partagé Atlas", Multiselect = false };
        if (dialog.ShowDialog() == true)
        {
            SharedRootBox.Text = dialog.FolderName;
            await RefreshModeAsync();
        }
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private static bool IsLegacyWorkspace(string root) =>
        File.Exists(Path.Combine(root, "Configuration", "users.atlas.json")) ||
        File.Exists(Path.Combine(root, "Data", "catalog.atlas.json"));
}
