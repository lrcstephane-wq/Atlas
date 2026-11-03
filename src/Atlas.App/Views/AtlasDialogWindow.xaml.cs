using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Atlas.App.Views;

public enum AtlasDialogTone { Info, Warning, Error, Confirm }
public enum AtlasDialogChoice { Cancel, Confirm, Third }

public partial class AtlasDialogWindow : Window
{
    public AtlasDialogWindow(string title, string message, string? detail, AtlasDialogTone tone, string? confirmLabel = null, string? cancelLabel = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        if (!string.IsNullOrWhiteSpace(detail)) { DetailText.Text = detail; DetailPanel.Visibility = Visibility.Visible; }
        var color = tone switch { AtlasDialogTone.Warning => "#F4B860", AtlasDialogTone.Error => "#F06A88", AtlasDialogTone.Confirm => "#2DD4BF", _ => "#397FF6" };
        ToneBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        ToneIcon.Text = tone switch { AtlasDialogTone.Warning => "!", AtlasDialogTone.Error => "×", AtlasDialogTone.Confirm => "?", _ => "i" };
        if (tone == AtlasDialogTone.Confirm)
        {
            CancelButton.Visibility = Visibility.Visible;
            ConfirmButton.Content = confirmLabel ?? "Confirmer";
            CancelButton.Content = cancelLabel ?? "Annuler";
            if (!string.IsNullOrWhiteSpace(cancelLabel)) CancelButton.IsDefault = true;
        }
    }

    public void EnableThirdChoice(string label)
    {
        ThirdButton.Content = label;
        ThirdButton.Visibility = Visibility.Visible;
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_OnClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Third_OnClick(object sender, RoutedEventArgs e) { Tag = AtlasDialogChoice.Third; DialogResult = false; Close(); }
    private void Close_OnClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public static class AtlasDialog
{
    public static void Info(string message, string title = "Information", string? detail = null) => Show(title, message, detail, AtlasDialogTone.Info);
    public static void Warning(string message, string title = "Attention", string? detail = null) => Show(title, message, detail, AtlasDialogTone.Warning);
    public static void Error(string message, string title = "Erreur", string? detail = null) => Show(title, message, detail, AtlasDialogTone.Error);
    public static bool Confirm(string message, string title = "Confirmation", string? detail = null) => Show(title, message, detail, AtlasDialogTone.Confirm) == true;
    public static bool Choose(string message, string title, string? detail, string confirmLabel, string cancelLabel) => Show(title, message, detail, AtlasDialogTone.Confirm, confirmLabel, cancelLabel) == true;
    public static AtlasDialogChoice ChooseThree(string message, string title, string? detail, string confirmLabel, string thirdLabel, string cancelLabel)
    {
        var dialog = new AtlasDialogWindow(title, message, detail, AtlasDialogTone.Confirm, confirmLabel, cancelLabel);
        dialog.EnableThirdChoice(thirdLabel);
        SetOwner(dialog);
        var result = dialog.ShowDialog();
        if (dialog.Tag is AtlasDialogChoice.Third) return AtlasDialogChoice.Third;
        return result == true ? AtlasDialogChoice.Confirm : AtlasDialogChoice.Cancel;
    }
    private static bool? Show(string title, string message, string? detail, AtlasDialogTone tone, string? confirmLabel = null, string? cancelLabel = null)
    {
        var dialog = new AtlasDialogWindow(title, message, detail, tone, confirmLabel, cancelLabel);
        SetOwner(dialog);
        return dialog.ShowDialog();
    }


    private static void SetOwner(Window dialog)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive && window != dialog)
                    ?? Application.Current?.MainWindow;
        if (owner is { IsVisible: true }) dialog.Owner = owner;
    }
}
