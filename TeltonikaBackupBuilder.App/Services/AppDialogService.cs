using System.Linq;
using System.Windows;
using TeltonikaBackupBuilder.App.Views;

namespace TeltonikaBackupBuilder.App.Services;

public static class AppDialogService
{
    public static void ShowInfo(string message, string title = "Hinweis")
    {
        Show(title, message, AppDialogSeverity.Info);
    }

    public static void ShowWarning(string message, string title = "Warnung")
    {
        Show(title, message, AppDialogSeverity.Warning);
    }

    public static void ShowError(string message, string title = "Fehler")
    {
        Show(title, message, AppDialogSeverity.Error);
    }

    private static void Show(string title, string message, AppDialogSeverity severity)
    {
        var dialog = new AppDialogWindow(title, message, severity);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;
        if (owner != null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }
}
