using System.Windows;
using Atlas.App.Views;
using Atlas.Core.Services;

namespace Atlas.App;

public partial class App : Application
{
    private async void App_OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            var bootstrap = await SharedCatalogStore.LoadBootstrapAsync();
            var login = new LoginWindow(bootstrap);
            if (login.ShowDialog() != true || login.AuthenticatedUser is null)
            {
                Shutdown();
                return;
            }

            var store = new SharedCatalogStore(login.SharedRoot);
            var userStore = new UserAccountStore(login.SharedRoot);
            var viewModel = new MainViewModel(store, userStore, bootstrap, login.AuthenticatedUser);
            await viewModel.InitializeAsync();
            var window = new MainWindow(viewModel);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception exception)
        {
            AtlasDialog.Error(exception.Message, "Démarrage impossible");
            Shutdown();
        }
    }
}
