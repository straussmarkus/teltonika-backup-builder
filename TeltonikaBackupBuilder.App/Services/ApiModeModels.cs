using System.Collections.Generic;

namespace TeltonikaBackupBuilder.App.Services;

public enum ApiCallKind
{
    ApplyConfigFile,
    GenerateBackup,
    DownloadBackup
}

public enum ApiRequestKind
{
    None,
    Json,
    MultipartFormData
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
    ApiRequestKind RequestKind,
    string? RequestBody,
    ApiCallKind Kind,
    IReadOnlyDictionary<string, string>? FormFields = null,
    string? UploadFilePath = null,
    bool ExpectsBinaryResponse = false);

public sealed record BackupConfigComparison(
    bool IsIdentical,
    IReadOnlyList<string> Differences);

public sealed record ApiCallExecutionResult(
    bool IsSuccess,
    int? StatusCode,
    string ResponseText,
    string? ErrorMessage,
    byte[]? ResponseBytes = null);

public sealed record RouterDeviceInfo(
    string? Model,
    string? FirmwareVersion,
    string RawResponse);
