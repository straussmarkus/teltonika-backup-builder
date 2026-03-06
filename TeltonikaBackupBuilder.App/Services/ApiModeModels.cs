using System.Collections.Generic;

namespace TeltonikaBackupBuilder.App.Services;

public enum ApiCallKind
{
    ApplyConfigFile,
    ReloadConfig,
    CreateBackup,
    ReadBackupBase64
}

public sealed record BackupConfigFile(string ArchivePath, byte[] Content);

public sealed record BackupConfigAnalysis(
    string SourceBackupPath,
    string? DeviceModel,
    string? FirmwareVersion,
    string DocumentationUrl,
    IReadOnlyList<BackupConfigFile> ConfigFiles);

public sealed record PlannedApiCall(
    int Order,
    string Name,
    string Method,
    string Path,
    string RequestBody,
    ApiCallKind Kind,
    string? CliCommand = null);

public sealed record BackupConfigComparison(
    bool IsIdentical,
    IReadOnlyList<string> Differences);

public sealed record ApiCallExecutionResult(
    bool IsSuccess,
    int? StatusCode,
    string ResponseText,
    string? ErrorMessage);

public sealed record RouterDeviceInfo(
    string? Model,
    string? FirmwareVersion,
    string RawResponse);
