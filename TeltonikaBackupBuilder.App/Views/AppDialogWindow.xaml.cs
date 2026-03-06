using System.Windows;
using System.Windows.Media;

namespace TeltonikaBackupBuilder.App.Views;

public enum AppDialogSeverity
{
    Info,
    Warning,
    Error
}

public partial class AppDialogWindow : Window
{
    public AppDialogWindow(string title, string message, AppDialogSeverity severity)
    {
        InitializeComponent();
        Title = title;
        MessageTextBlock.Text = message;
        ApplySeverity(severity);
    }

    private void ApplySeverity(AppDialogSeverity severity)
    {
        switch (severity)
        {
            case AppDialogSeverity.Warning:
                SeverityBadge.Background = new SolidColorBrush(Color.FromRgb(193, 122, 0));
                SeverityGlyph.Text = "!";
                break;
            case AppDialogSeverity.Error:
                SeverityBadge.Background = new SolidColorBrush(Color.FromRgb(176, 48, 48));
                SeverityGlyph.Text = "X";
                break;
            default:
                SeverityBadge.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                SeverityGlyph.Text = "i";
                break;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
