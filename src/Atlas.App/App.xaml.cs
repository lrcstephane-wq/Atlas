using System.Windows;
using Atlas.App.Views;
using Atlas.Core.Models;
using Atlas.Core.Services;

namespace Atlas.App;

public partial class App : Application
{
    private LocalBootstrap _bootstrap = new();

    private async void App_OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _bootstrap = await SharedCatalogStore.LoadBootstrapAsync();
            while (true)
            {
                var launch = new LaunchWindow();
                if (launch.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                if (launch.SelectedMode == AtlasEntryMode.User)
                {
                    if (string.IsNullOrWhiteSpace(_bootstrap.SharedRoot))
                    {
                        AtlasDialog.Warning("L’espace partagé Atlas doit d’abord être configuré par un administrateur.", "Horizon indisponible");
                        continue;
                    }
                    await OpenUserSessionAsync();
                    return;
                }

                var login = new LoginWindow(_bootstrap);
                if (login.ShowDialog() != true || login.AuthenticatedUser is null) continue;
                await OpenSessionAsync(login.AuthenticatedUser, false);
                return;
            }
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Démarrage impossible");
            Shutdown();
        }
    }

    public async Task OpenAdministratorSessionAsync(MainWindow currentWindow)
    {
        try
        {
            var login = new LoginWindow(_bootstrap) { Owner = currentWindow };
            if (login.ShowDialog() != true || login.AuthenticatedUser is null) return;
            await OpenSessionAsync(login.AuthenticatedUser, false, currentWindow);
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Connexion administrateur impossible");
        }
    }

    public async Task LogoutAdministratorAsync(MainWindow currentWindow)
    {
        try { await OpenUserSessionAsync(currentWindow); }
        catch (Exception exception) { AtlasDialog.Error(exception.Message, "Déconnexion impossible"); }
    }

    private Task OpenUserSessionAsync(MainWindow? currentWindow = null)
    {
        var user = new UserAccount
        {
            Id = "atlas-horizon-user",
            Login = "horizon",
            DisplayName = "Utilisateur",
            Permissions = UserPermissions.Read,
            IsActive = true
        };
        return OpenSessionAsync(user, true, currentWindow);
    }

    private async Task OpenSessionAsync(UserAccount user, bool userMode, MainWindow? currentWindow = null)
    {
        var store = new SharedCatalogStore(_bootstrap.SharedRoot);
        var userStore = new UserAccountStore(_bootstrap.SharedRoot);
        var viewModel = new MainViewModel(store, userStore, _bootstrap, user, userMode);
        await viewModel.InitializeAsync();
        var window = new MainWindow(viewModel);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        window.Show();
        currentWindow?.Close();
    }
}
