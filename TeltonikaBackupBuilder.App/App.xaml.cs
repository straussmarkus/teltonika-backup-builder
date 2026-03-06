using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TeltonikaBackupBuilder.App.Services;

namespace TeltonikaBackupBuilder.App;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var details = BuildDetails("DispatcherUnhandledException", e.Exception);
        WriteCrashLog(details);
        AppDialogService.ShowError(
            $"Unerwarteter Fehler: {e.Exception.Message}{Environment.NewLine}{Environment.NewLine}Details in:{Environment.NewLine}{CrashLogPath}",
            "Anwendungsfehler");
        e.Handled = true;
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var details = BuildDetails("CurrentDomainUnhandledException", ex, e.IsTerminating);
        WriteCrashLog(details);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var details = BuildDetails("UnobservedTaskException", e.Exception);
        WriteCrashLog(details);
        e.SetObserved();
    }

    private static string BuildDetails(string source, Exception? exception, bool isTerminating = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== TeltonikaBackupBuilder Crash =====");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine($"IsTerminating: {isTerminating}");
        sb.AppendLine($"Runtime: {Environment.Version}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
        sb.AppendLine();
        sb.AppendLine(exception?.ToString() ?? "Exception: <null>");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void WriteCrashLog(string details)
    {
        try
        {
            File.AppendAllText(CrashLogPath, details, Encoding.UTF8);
        }
        catch
        {
            // ignore logging failures
        }
    }
}
