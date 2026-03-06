using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TeltonikaBackupBuilder.App.Services;

public sealed class ApiCallPlannerService
{
    private const string DefaultCliEndpoint = "/api/cli/actions/execute";
    private const string DefaultRemoteBackupPath = "/tmp/api-rebuild-backup.tar.gz";

    public IReadOnlyList<PlannedApiCall> CreatePlan(
        BackupConfigAnalysis analysis,
        string? cliEndpointPath,
        string? remoteBackupPath = null)
    {
        if (analysis.ConfigFiles.Count == 0)
        {
            throw new InvalidOperationException("Es wurden keine Konfigurationsdateien für den API-Plan gefunden.");
        }

        var endpointPath = string.IsNullOrWhiteSpace(cliEndpointPath)
            ? DefaultCliEndpoint
            : NormalizePath(cliEndpointPath);
        var backupPath = string.IsNullOrWhiteSpace(remoteBackupPath)
            ? DefaultRemoteBackupPath
            : remoteBackupPath.Trim();

        var calls = new List<PlannedApiCall>();
        var order = 1;

        foreach (var file in analysis.ConfigFiles)
        {
            var configName = file.ArchivePath["etc/config/".Length..];
            var payloadBase64 = Convert.ToBase64String(file.Content);
            var command = $"printf '%s' '{payloadBase64}' | base64 -d > '/etc/config/{configName}' && uci commit '{configName}'";
            calls.Add(new PlannedApiCall(
                order++,
                $"Apply {file.ArchivePath}",
                "POST",
                endpointPath,
                BuildCliRequestBody(command),
                ApiCallKind.ApplyConfigFile,
                command));
        }

        var reloadCommand = "reload_config";
        calls.Add(new PlannedApiCall(
            order++,
            "Reload configuration",
            "POST",
            endpointPath,
            BuildCliRequestBody(reloadCommand),
            ApiCallKind.ReloadConfig,
            reloadCommand));

        var createBackupCommand = $"sysupgrade -b '{backupPath}'";
        calls.Add(new PlannedApiCall(
            order++,
            "Create router backup",
            "POST",
            endpointPath,
            BuildCliRequestBody(createBackupCommand),
            ApiCallKind.CreateBackup,
            createBackupCommand));

        var readBackupCommand = $"base64 -w 0 '{backupPath}'";
        calls.Add(new PlannedApiCall(
            order,
            "Read router backup as base64",
            "POST",
            endpointPath,
            BuildCliRequestBody(readBackupCommand),
            ApiCallKind.ReadBackupBase64,
            readBackupCommand));

        return calls;
    }

    private static string BuildCliRequestBody(string command)
    {
        var payload = new
        {
            data = new
            {
                command
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }
}
