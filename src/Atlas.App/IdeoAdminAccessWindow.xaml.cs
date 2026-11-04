using System.IO;
using System.Windows;
using Atlas.Core.Services;

namespace Atlas.App;

public partial class IdeoAdminAccessWindow : Window
{
    private readonly IdeoAdminChallenge _challenge;

    public IdeoAdminAccessWindow()
    {
        InitializeComponent();
        _challenge = IdeoAdminAccessService.CreateChallenge();
        DeviceIdBox.Text = MachineIdentityService.CurrentDeviceId;
        RequestCodeBox.Text = _challenge.RequestCode;
    }

    public IdeoAdminAccessGrant? Grant { get; private set; }

    private void CopyRequest_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(RequestCodeBox.Text);
        ErrorText.Text = "Demande copiée. Le code reçu ne fonctionnera que dans cette fenêtre.";
    }

    private void Unlock_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Grant = _challenge.ValidateAndConsume(ResponseCodeBox.Text);
            AppendAudit(Grant);
            DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }

    private static void AppendAudit(IdeoAdminAccessGrant grant)
    {
        Directory.CreateDirectory(AtlasPaths.LogsRoot);
        File.AppendAllText(Path.Combine(AtlasPaths.LogsRoot, "ideo-admin-access.log"),
            $"{DateTimeOffset.Now:O}\t{grant.OperatorName}\t{MachineIdentityService.CurrentDeviceId}\t{grant.TokenId}\tOPEN{Environment.NewLine}");
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
