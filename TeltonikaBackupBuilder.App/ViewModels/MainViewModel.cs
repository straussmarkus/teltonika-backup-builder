using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly BackupConfigAnalysisService _analysisService = new();
    private readonly ApiCallPlannerService _plannerService = new();

    private BackupConfigAnalysis? _apiAnalysis;

    private string _sourceBackupPath = string.Empty;
    private string _hostnamesInput = string.Empty;
    private string _outputDirectory = string.Empty;
    private bool _isBusy;
    private bool _verifyAfterCreate = true;

    private bool _isApiBusy;
    private string _routerIpAddress = "192.168.2.1";
    private string _routerUsername = "admin";
    private string _routerPassword = string.Empty;
    private string _documentationUrl = string.Empty;
    private string _backupModel = "Unbekannt";
    private string _backupFirmware = "Unbekannt";
    private ApiCallItemViewModel? _selectedApiCall;
    private string _selectedApiCallRequest = string.Empty;
    private string _selectedApiCallResponse = string.Empty;

    public MainViewModel()
    {
        BrowseSourceCommand = new RelayCommand(BrowseSource);
        GenerateCommand = new RelayCommand(Generate, CanGenerate);
        AnalyzeApiPlanCommand = new RelayCommand(AnalyzeApiPlan, CanAnalyzeApiPlan);
        ExecuteSelectedApiCallCommand = new RelayCommand(ExecuteSelectedApiCall, CanExecuteSelectedApiCall);
        ExecuteAllApiCallsCommand = new RelayCommand(ExecuteAllApiCalls, CanExecuteAllApiCalls);

        LogEntries = new ObservableCollection<string>();
        ApiLogEntries = new ObservableCollection<string>();
        ApiCalls = new ObservableCollection<ApiCallItemViewModel>();
        ApiCalls.CollectionChanged += (_, _) => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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

    public bool IsApiBusy
    {
        get => _isApiBusy;
        private set => SetProperty(ref _isApiBusy, value);
    }

    public string RouterIpAddress
    {
        get => _routerIpAddress;
        set
        {
            if (SetProperty(ref _routerIpAddress, value) && SelectedApiCall != null)
            {
                SelectedApiCallRequest = BuildRequestPreview(SelectedApiCall.Call, _routerIpAddress);
            }
        }
    }

    public string RouterUsername
    {
        get => _routerUsername;
        set => SetProperty(ref _routerUsername, value);
    }

    public string RouterPassword
    {
        get => _routerPassword;
        set => SetProperty(ref _routerPassword, value);
    }

    public string DocumentationUrl
    {
        get => _documentationUrl;
        private set => SetProperty(ref _documentationUrl, value);
    }

    public string BackupModel
    {
        get => _backupModel;
        private set => SetProperty(ref _backupModel, value);
    }

    public string BackupFirmware
    {
        get => _backupFirmware;
        private set => SetProperty(ref _backupFirmware, value);
    }

    public ApiCallItemViewModel? SelectedApiCall
    {
        get => _selectedApiCall;
        set
        {
            if (SetProperty(ref _selectedApiCall, value))
            {
                SelectedApiCallRequest = value == null
                    ? string.Empty
                    : BuildRequestPreview(value.Call, RouterIpAddress);
                SelectedApiCallResponse = value?.Response ?? string.Empty;
            }
        }
    }

    public string SelectedApiCallRequest
    {
        get => _selectedApiCallRequest;
        private set => SetProperty(ref _selectedApiCallRequest, value);
    }

    public string SelectedApiCallResponse
    {
        get => _selectedApiCallResponse;
        private set => SetProperty(ref _selectedApiCallResponse, value);
    }

    public ObservableCollection<string> LogEntries { get; }

    public ObservableCollection<string> ApiLogEntries { get; }

    public ObservableCollection<ApiCallItemViewModel> ApiCalls { get; }

    public RelayCommand BrowseSourceCommand { get; }

    public RelayCommand GenerateCommand { get; }

    public RelayCommand AnalyzeApiPlanCommand { get; }

    public RelayCommand ExecuteSelectedApiCallCommand { get; }

    public RelayCommand ExecuteAllApiCallsCommand { get; }

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
            AppDialogService.ShowWarning(error ?? "Ungueltige Eingabe.", "Fehler");
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceBackupPath) || !File.Exists(SourceBackupPath))
        {
            AppDialogService.ShowWarning("Bitte ein gueltiges Backup auswaehlen.", "Fehler");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            AppDialogService.ShowWarning("Kein Ausgabeverzeichnis gefunden.", "Fehler");
            return;
        }

        var outputFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingHostname = _backupService.GetExistingHostname(SourceBackupPath);
        foreach (var hostname in hostnames)
        {
            var outputFileName = BackupCloneService.BuildOutputFileName(SourceBackupPath, existingHostname, hostname);
            if (!outputFileNames.Add(outputFileName))
            {
                AppDialogService.ShowWarning($"Der erzeugte Dateiname '{outputFileName}' ist doppelt. Bitte Hostnamen pruefen.",
                    "Fehler");
                return;
            }

            var outputPath = Path.Combine(OutputDirectory, outputFileName);
            if (File.Exists(outputPath))
            {
                AppDialogService.ShowWarning($"Die Datei '{outputFileName}' existiert bereits. Bitte loeschen oder Hostnamen aendern.",
                    "Fehler");
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
            AppDialogService.ShowError(ex.Message, "Fehler");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAnalyzeApiPlan()
    {
        return !IsApiBusy
            && !string.IsNullOrWhiteSpace(SourceBackupPath)
            && File.Exists(SourceBackupPath);
    }

    private async void AnalyzeApiPlan()
    {
        if (string.IsNullOrWhiteSpace(SourceBackupPath) || !File.Exists(SourceBackupPath))
        {
            AppDialogService.ShowWarning("Bitte ein gueltiges Quellbackup auswaehlen.", "Fehler");
            return;
        }

        IsApiBusy = true;
        ApiLogEntries.Clear();
        ApiCalls.Clear();
        SelectedApiCall = null;

        try
        {
            var analysis = await Task.Run(() => _analysisService.Analyze(SourceBackupPath));
            _apiAnalysis = analysis;

            BackupModel = analysis.DeviceModel ?? "Unbekannt";
            BackupFirmware = analysis.FirmwareVersion ?? "Unbekannt";
            DocumentationUrl = analysis.DocumentationUrl;

            var plan = _plannerService.CreatePlan(analysis);
            foreach (var plannedCall in plan)
            {
                ApiCalls.Add(new ApiCallItemViewModel(plannedCall));
            }

            SelectedApiCall = ApiCalls.FirstOrDefault();
            ApiLogEntries.Add($"Analyse abgeschlossen. {ApiCalls.Count} API-Calls geplant.");
            ApiLogEntries.Add($"Doku-Kandidat: {DocumentationUrl}");
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError(ex.Message, "Fehler");
        }
        finally
        {
            IsApiBusy = false;
        }
    }

    private bool CanExecuteSelectedApiCall()
    {
        return !IsApiBusy
            && SelectedApiCall != null
            && HasConnectionData()
            && ApiCalls.Count > 0;
    }

    private bool CanExecuteAllApiCalls()
    {
        return !IsApiBusy
            && HasConnectionData()
            && ApiCalls.Count > 0;
    }

    private async void ExecuteSelectedApiCall()
    {
        if (SelectedApiCall == null)
        {
            return;
        }

        await ExecuteApiCallsAsync(new[] { SelectedApiCall }, verifyAfterRun: true);
    }

    private async void ExecuteAllApiCalls()
    {
        var calls = ApiCalls.OrderBy(c => c.Order).ToList();
        await ExecuteApiCallsAsync(calls, verifyAfterRun: true);
    }

    private async Task ExecuteApiCallsAsync(IReadOnlyList<ApiCallItemViewModel> callsToRun, bool verifyAfterRun)
    {
        if (!HasConnectionData())
        {
            AppDialogService.ShowWarning("Bitte IP-Adresse und Benutzername fuer die Router-Verbindung eintragen.",
                "Fehler");
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceBackupPath) || !File.Exists(SourceBackupPath))
        {
            AppDialogService.ShowWarning("Bitte ein gueltiges Quellbackup auswaehlen.", "Fehler");
            return;
        }

        IsApiBusy = true;
        try
        {
            using var client = new RouterApiClient(RouterIpAddress);
            var token = await client.LoginAsync(RouterUsername, RouterPassword, CancellationToken.None);
            ApiLogEntries.Add($"Login OK ({client.BaseUrl}).");

            var deviceInfo = await client.GetDeviceInfoAsync(token, CancellationToken.None);
            if (_apiAnalysis != null)
            {
                var backupModel = _apiAnalysis.DeviceModel;
                var backupFirmware = _apiAnalysis.FirmwareVersion;
                if (!string.IsNullOrWhiteSpace(deviceInfo.Model)
                    && !string.IsNullOrWhiteSpace(backupModel)
                    && !string.Equals(deviceInfo.Model, backupModel, StringComparison.OrdinalIgnoreCase))
                {
                    ApiLogEntries.Add($"WARNUNG: Router-Modell '{deviceInfo.Model}' passt nicht zum Backup-Modell '{backupModel}'.");
                }

                if (!string.IsNullOrWhiteSpace(deviceInfo.FirmwareVersion)
                    && !string.IsNullOrWhiteSpace(backupFirmware)
                    && !string.Equals(deviceInfo.FirmwareVersion, backupFirmware, StringComparison.OrdinalIgnoreCase))
                {
                    ApiLogEntries.Add($"WARNUNG: Router-Firmware '{deviceInfo.FirmwareVersion}' passt nicht zur Backup-Firmware '{backupFirmware}'.");
                }
            }

            string? generatedBackupPath = null;

            foreach (var callItem in callsToRun.OrderBy(c => c.Order))
            {
                callItem.Status = "Läuft...";
                var result = await ExecuteCallWithRetryAsync(client, callItem.Call, token, CancellationToken.None);

                var responseSummary = BuildResponseSummary(result);
                var detailedResponse = BuildResponseDetails(result);
                callItem.Response = detailedResponse;
                callItem.IsSuccess = result.IsSuccess;
                callItem.Status = result.IsSuccess ? "OK" : "Fehler";
                ApiLogEntries.Add($"[{callItem.Order}] {callItem.Name}: {responseSummary}");

                if (ReferenceEquals(callItem, SelectedApiCall))
                {
                    SelectedApiCallResponse = responseSummary + Environment.NewLine + Environment.NewLine + detailedResponse;
                    SelectedApiCallRequest = BuildRequestPreview(callItem.Call, RouterIpAddress);
                }

                if (!result.IsSuccess)
                {
                    break;
                }

                if (callItem.Kind == ApiCallKind.DownloadBackup)
                {
                    if (result.ResponseBytes == null || !IsValidGzipArchive(result.ResponseBytes))
                    {
                        throw new InvalidOperationException("Die Download-Antwort enthielt kein gueltiges .tar.gz Backup.");
                    }

                    generatedBackupPath = BuildApiGeneratedBackupPath(SourceBackupPath);
                    await File.WriteAllBytesAsync(generatedBackupPath, result.ResponseBytes, CancellationToken.None);
                    ApiLogEntries.Add($"Router-Backup gespeichert: {generatedBackupPath}");
                }
            }

            if (verifyAfterRun && generatedBackupPath != null)
            {
                var comparison = _analysisService.CompareConfigFolders(SourceBackupPath, generatedBackupPath);
                if (comparison.IsIdentical)
                {
                    ApiLogEntries.Add("Verifikation OK: /etc/config ist zwischen Original und API-Resultat identisch.");
                }
                else
                {
                    ApiLogEntries.Add("Verifikation fehlgeschlagen:");
                    foreach (var difference in comparison.Differences.Take(20))
                    {
                        ApiLogEntries.Add(" - " + difference);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppDialogService.ShowError(ex.Message, "Fehler");
        }
        finally
        {
            IsApiBusy = false;
        }
    }

    private static string BuildResponseSummary(ApiCallExecutionResult result)
    {
        if (result.IsSuccess)
        {
            return result.StatusCode.HasValue
                ? $"OK (HTTP {result.StatusCode.Value})"
                : "OK";
        }

        var statusText = result.StatusCode.HasValue ? $"HTTP {result.StatusCode.Value}" : "kein HTTP-Status";
        var error = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Unbekannter Fehler" : result.ErrorMessage;
        return $"Fehler ({statusText}): {error}";
    }

    private bool HasConnectionData()
    {
        return !string.IsNullOrWhiteSpace(RouterIpAddress)
            && !string.IsNullOrWhiteSpace(RouterUsername);
    }

    private static string BuildRequestPreview(PlannedApiCall call, string routerAddress)
    {
        var normalizedAddress = routerAddress.Trim();
        if (!normalizedAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !normalizedAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAddress = "https://" + normalizedAddress;
        }

        normalizedAddress = normalizedAddress.TrimEnd('/');
        var path = call.Path.StartsWith("/", StringComparison.Ordinal) ? call.Path : "/" + call.Path;
        var url = normalizedAddress + path;

        var sb = new StringBuilder();
        sb.AppendLine($"{call.Method} {url}");
        sb.AppendLine("Authorization: Bearer <token>");

        switch (call.RequestKind)
        {
            case ApiRequestKind.None:
                break;
            case ApiRequestKind.Json:
                sb.AppendLine("Content-Type: application/json");
                sb.AppendLine();
                sb.AppendLine(FormatJson(call.RequestBody));
                break;
            case ApiRequestKind.MultipartFormData:
                sb.AppendLine("Content-Type: multipart/form-data");
                sb.AppendLine();
                sb.AppendLine("form-data:");
                if (call.FormFields != null)
                {
                    foreach (var field in call.FormFields)
                    {
                        sb.AppendLine($"  {field.Key}: {field.Value}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(call.UploadFilePath))
                {
                    sb.AppendLine($"  file: @{call.UploadFilePath}");
                }
                break;
            default:
                throw new InvalidOperationException($"Unbekannter Request-Typ: {call.RequestKind}");
        }

        return sb.ToString();
    }

    private static string FormatJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string BuildApiGeneratedBackupPath(string sourceBackupPath)
    {
        var directory = Path.GetDirectoryName(sourceBackupPath) ?? Environment.CurrentDirectory;
        var fileName = Path.GetFileName(sourceBackupPath);
        var baseName = fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^7]
            : Path.GetFileNameWithoutExtension(fileName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{baseName}-api-result-{timestamp}.tar.gz");
    }

    private static async Task<ApiCallExecutionResult> ExecuteCallWithRetryAsync(
        RouterApiClient client,
        PlannedApiCall call,
        string token,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await client.ExecuteCallAsync(call, token, cancellationToken);
            if (result.IsSuccess || call.Kind != ApiCallKind.DownloadBackup || result.StatusCode != 404 || attempt == maxAttempts)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return new ApiCallExecutionResult(false, null, string.Empty, "Download-Call konnte nicht ausgefuehrt werden.");
    }

    private static string BuildResponseDetails(ApiCallExecutionResult result)
    {
        if (result.ResponseBytes == null)
        {
            return result.ResponseText;
        }

        var sha = Convert.ToHexString(SHA256.HashData(result.ResponseBytes));
        return $"Binary response: {result.ResponseBytes.Length} bytes{Environment.NewLine}SHA256: {sha}";
    }

    private static bool IsValidGzipArchive(byte[] bytes)
    {
        return bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
    }

}
