using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using TeltonikaBackupBuilder.App.Helpers;
using TeltonikaBackupBuilder.App.Services;

namespace TeltonikaBackupBuilder.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly BackupCloneService _backupService = new();

    private string _sourceBackupPath = string.Empty;
    private string _hostnamesInput = string.Empty;
    private string _outputDirectory = string.Empty;
    private bool _isBusy;
    private bool _verifyAfterCreate = true;

    public MainViewModel()
    {
        BrowseSourceCommand = new RelayCommand(BrowseSource);
        GenerateCommand = new RelayCommand(Generate, CanGenerate);
        LogEntries = new ObservableCollection<string>();
    }

    public string SourceBackupPath
    {
        get => _sourceBackupPath;
        set
        {
            if (SetProperty(ref _sourceBackupPath, value))
            {
                OutputDirectory = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : Path.GetDirectoryName(value) ?? string.Empty;
            }
        }
    }

    public string HostnamesInput
    {
        get => _hostnamesInput;
        set => SetProperty(ref _hostnamesInput, value);
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        private set => SetProperty(ref _outputDirectory, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool VerifyAfterCreate
    {
        get => _verifyAfterCreate;
        set => SetProperty(ref _verifyAfterCreate, value);
    }

    public ObservableCollection<string> LogEntries { get; }

    public RelayCommand BrowseSourceCommand { get; }

    public RelayCommand GenerateCommand { get; }

    private void BrowseSource()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Teltonika Backups (*.tar.gz)|*.tar.gz|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SourceBackupPath = dialog.FileName;
        }
    }

    private bool CanGenerate()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(SourceBackupPath)
            && File.Exists(SourceBackupPath)
            && !string.IsNullOrWhiteSpace(HostnamesInput);
    }

    private async void Generate()
    {
        if (!HostnameParser.TryParse(HostnamesInput, out var hostnames, out var error))
        {
            MessageBox.Show(error ?? "Ungültige Eingabe.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceBackupPath) || !File.Exists(SourceBackupPath))
        {
            MessageBox.Show("Bitte ein gültiges Backup auswählen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            MessageBox.Show("Kein Ausgabeverzeichnis gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outputFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingHostname = _backupService.GetExistingHostname(SourceBackupPath);
        foreach (var hostname in hostnames)
        {
            var outputFileName = BackupCloneService.BuildOutputFileName(SourceBackupPath, existingHostname, hostname);
            if (!outputFileNames.Add(outputFileName))
            {
                MessageBox.Show($"Der erzeugte Dateiname '{outputFileName}' ist doppelt. Bitte Hostnamen prüfen.",
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var outputPath = Path.Combine(OutputDirectory, outputFileName);
            if (File.Exists(outputPath))
            {
                MessageBox.Show($"Die Datei '{outputFileName}' existiert bereits. Bitte löschen oder Hostnamen ändern.",
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        LogEntries.Clear();
        IsBusy = true;

        try
        {
            IProgress<string> progress = new Progress<string>(message => LogEntries.Add(message));
            var verifyAfterCreate = VerifyAfterCreate;

            await Task.Run(() =>
            {
                _backupService.GenerateBackups(SourceBackupPath, OutputDirectory, hostnames, existingHostname, progress, CancellationToken.None);

                if (!verifyAfterCreate)
                {
                    return;
                }

                foreach (var hostname in hostnames)
                {
                    var outputFileName = BackupCloneService.BuildOutputFileName(SourceBackupPath, existingHostname, hostname);
                    var outputPath = Path.Combine(OutputDirectory, outputFileName);
                    var result = _backupService.VerifyBackup(SourceBackupPath, outputPath);

                    if (!result.IsValid)
                    {
                        var details = result.Differences.Count == 0
                            ? "Unbekannter Fehler."
                            : string.Join(", ", result.Differences);
                        throw new InvalidOperationException($"Verifikation fehlgeschlagen für '{outputFileName}': {details}");
                    }

                    var message = result.SystemChanged
                        ? $"Verifikation OK: {outputFileName} (nur /etc/config/system geändert)."
                        : $"Verifikation OK: {outputFileName} (keine Änderungen festgestellt).";
                    progress.Report(message);
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
