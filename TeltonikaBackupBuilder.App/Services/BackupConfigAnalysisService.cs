using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace TeltonikaBackupBuilder.App.Services;

public sealed class BackupConfigAnalysisService
{
    private const string ConfigRoot = "etc/config/";
    private const string SystemConfigPath = "etc/config/system";

    public BackupConfigAnalysis Analyze(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup-Datei nicht gefunden.", backupPath);
        }

        var configFiles = new List<BackupConfigFile>();
        byte[]? systemBytes = null;

        using var sourceStream = File.OpenRead(backupPath);
        using var gzip = new GZipStream(sourceStream, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            if (entry.EntryType != TarEntryType.RegularFile)
            {
                continue;
            }

            var normalizedName = entry.Name.Replace('\\', '/');
            if (!normalizedName.StartsWith(ConfigRoot, StringComparison.Ordinal))
            {
                continue;
            }

            var bytes = ReadEntryBytes(entry);
            configFiles.Add(new BackupConfigFile(normalizedName, bytes));

            if (string.Equals(normalizedName, SystemConfigPath, StringComparison.Ordinal))
            {
                systemBytes = bytes;
            }
        }

        if (configFiles.Count == 0)
        {
            throw new InvalidOperationException("Im Backup wurden keine /etc/config Dateien gefunden.");
        }

        var orderedConfigFiles = configFiles
            .OrderBy(c => c.ArchivePath, StringComparer.Ordinal)
            .ToList();

        var firmwareVersion = systemBytes == null
            ? null
            : SystemConfigEditor.TryGetOptionValue(systemBytes, "device_fw_version");
        var deviceCode = systemBytes == null
            ? null
            : SystemConfigEditor.TryGetOptionValue(systemBytes, "device_code");
        var model = ParseModelFromDeviceCode(deviceCode);
        var documentationUrl = BuildDocumentationUrl(model, firmwareVersion);

        return new BackupConfigAnalysis(
            backupPath,
            model,
            firmwareVersion,
            documentationUrl,
            orderedConfigFiles);
    }

    public BackupConfigComparison CompareConfigFolders(string originalBackupPath, string generatedBackupPath)
    {
        var original = Analyze(originalBackupPath);
        var generated = Analyze(generatedBackupPath);

        var differences = new List<string>();
        var originalMap = original.ConfigFiles.ToDictionary(c => c.ArchivePath, c => c.Content, StringComparer.Ordinal);
        var generatedMap = generated.ConfigFiles.ToDictionary(c => c.ArchivePath, c => c.Content, StringComparer.Ordinal);

        foreach (var (path, originalContent) in originalMap)
        {
            if (!generatedMap.TryGetValue(path, out var generatedContent))
            {
                differences.Add($"Fehlt im Zielbackup: {path}");
                continue;
            }

            if (!originalContent.AsSpan().SequenceEqual(generatedContent))
            {
                differences.Add($"Inhalt unterschiedlich: {path}");
            }
        }

        foreach (var path in generatedMap.Keys)
        {
            if (!originalMap.ContainsKey(path))
            {
                differences.Add($"Zusätzliche Datei im Zielbackup: {path}");
            }
        }

        return new BackupConfigComparison(differences.Count == 0, differences);
    }

    private static byte[] ReadEntryBytes(TarEntry entry)
    {
        if (entry.DataStream == null)
        {
            return Array.Empty<byte>();
        }

        using var ms = new MemoryStream();
        entry.DataStream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? ParseModelFromDeviceCode(string? deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return null;
        }

        var match = Regex.Match(deviceCode, "^[A-Za-z]{3}[0-9]{3}", RegexOptions.CultureInvariant);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static string BuildDocumentationUrl(string? model, string? firmwareVersion)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "https://developers.teltonika-networks.com/";
        }

        var modelSegment = model.ToLowerInvariant();
        var parsedFirmware = ParseFirmwareForDocs(firmwareVersion);
        if (parsedFirmware == null)
        {
            return $"https://developers.teltonika-networks.com/reference/{modelSegment}/";
        }

        return $"https://developers.teltonika-networks.com/reference/{modelSegment}/{parsedFirmware}/";
    }

    private static string? ParseFirmwareForDocs(string? firmwareVersion)
    {
        if (string.IsNullOrWhiteSpace(firmwareVersion))
        {
            return null;
        }

        var match = Regex.Match(firmwareVersion, "[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+", RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }
}
