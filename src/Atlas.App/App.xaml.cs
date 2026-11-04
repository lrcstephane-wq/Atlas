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
            if (!await SelectAndOpenRoleAsync()) Shutdown();
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Démarrage impossible");
            Shutdown();
        }
    }

    public async Task ChangeRoleAsync(MainWindow currentWindow)
    {
        try { await SelectAndOpenRoleAsync(currentWindow); }
        catch (Exception exception) { AtlasDialog.Error(exception.Message, "Changement de rôle impossible"); }
    }

    private async Task<bool> SelectAndOpenRoleAsync(MainWindow? currentWindow = null)
    {
        while (true)
        {
            var launch = new LaunchWindow { Owner = currentWindow };
            if (launch.ShowDialog() != true) return false;

            if (launch.SelectedMode == AtlasEntryMode.User)
            {
                if (string.IsNullOrWhiteSpace(_bootstrap.SharedRoot) || !AtlasWorkspaceService.IsWorkspace(_bootstrap.SharedRoot))
                {
                    AtlasDialog.Warning("L’espace Atlas doit d’abord être créé ou sélectionné par un Gestionnaire du site.", "Horizon indisponible");
                    continue;
                }
                await OpenUserSessionAsync(currentWindow);
                return true;
            }

            if (launch.SelectedMode == AtlasEntryMode.IdeoAdministrator)
            {
                if (await OpenIdeoAdministratorSessionAsync(currentWindow)) return true;
                continue;
            }

            var login = new LoginWindow(_bootstrap) { Owner = currentWindow };
            if (login.ShowDialog() != true || login.AuthenticatedUser is null)
            {
                if (login.IdeoAccessRequested && await OpenIdeoAdministratorSessionAsync(currentWindow)) return true;
                continue;
            }
            if (launch.SelectedMode == AtlasEntryMode.Creator && !login.AuthenticatedUser.IsCreator && !login.AuthenticatedUser.IsAdministrator)
            {
                AtlasDialog.Warning("Ce compte ne possède pas le profil Créateur.", "Accès refusé");
                continue;
            }
            if (launch.SelectedMode == AtlasEntryMode.SiteManager && !login.AuthenticatedUser.IsSiteManager)
            {
                AtlasDialog.Warning("Ce compte ne possède pas le profil Gestionnaire du site.", "Accès refusé");
                continue;
            }
            await OpenSessionAsync(login.AuthenticatedUser, false, currentWindow);
            return true;
        }
    }

    public async Task OpenAdministratorSessionAsync(MainWindow currentWindow)
    {
        try
        {
            var login = new LoginWindow(_bootstrap) { Owner = currentWindow };
            if (login.ShowDialog() != true || login.AuthenticatedUser is null)
            {
                if (login.IdeoAccessRequested) await OpenIdeoAdministratorSessionAsync(currentWindow);
                return;
            }
            if (!login.AuthenticatedUser.IsSiteManager)
            {
                AtlasDialog.Warning("Ce compte ne possède pas le profil Gestionnaire du site.", "Accès refusé");
                return;
            }
            await OpenSessionAsync(login.AuthenticatedUser, false, currentWindow);
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Connexion sécurisée impossible");
        }
    }

    public async Task LogoutAdministratorAsync(MainWindow currentWindow)
    {
        try { await OpenUserSessionAsync(currentWindow); }
        catch (Exception exception) { AtlasDialog.Error(exception.Message, "Déconnexion impossible"); }
    }

    public async Task<bool> OpenIdeoAdministratorSessionAsync(MainWindow? currentWindow = null)
    {
        var trustedKeyPath = AtlasLicenseService.FindMatchingSigningPrivateKeyPath(_bootstrap.SharedRoot);
        if (trustedKeyPath is not null)
        {
            await OpenIdeoSessionAsync($"{Environment.UserName} · Développement Idéo", Guid.NewGuid().ToString("N"), true, currentWindow);
            return true;
        }

        var access = new IdeoAdminAccessWindow { Owner = currentWindow };
        if (access.ShowDialog() != true || access.Grant is null) return false;
        await OpenIdeoSessionAsync(access.Grant.OperatorName, access.Grant.TokenId, false, currentWindow);
        return true;
    }

    private async Task OpenIdeoSessionAsync(string displayName, string tokenId, bool permanentDevelopmentSession, MainWindow? currentWindow)
    {
        var user = new UserAccount
        {
            Id = $"ideo-{tokenId}",
            Login = "ideo",
            DisplayName = displayName,
            Permissions = UserPermissions.Read | UserPermissions.Edit | UserPermissions.Validate | UserPermissions.Administer
                          | UserPermissions.ManageOwnFurniture | UserPermissions.ManageSite | UserPermissions.IdeoAdministrator,
            IsActive = true,
            IsPermanentIdeoDevelopmentSession = permanentDevelopmentSession
        };
        await OpenSessionAsync(user, false, currentWindow);
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
