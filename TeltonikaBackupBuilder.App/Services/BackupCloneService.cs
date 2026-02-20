using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TeltonikaBackupBuilder.App.Services;

public sealed class BackupCloneService
{
    private const string SystemConfigPath = "etc/config/system";

    public string? GetExistingHostname(string sourcePath)
    {
        var template = LoadTarTemplate(sourcePath);
        var systemBytes = template.GetSystemDataBytes();
        return SystemConfigEditor.TryGetOptionValue(systemBytes, "devicename")
            ?? SystemConfigEditor.TryGetOptionValue(systemBytes, "hostname");
    }

    public void GenerateBackups(
        string sourcePath,
        string outputDirectory,
        IReadOnlyList<string> hostnames,
        string? existingHostname,
        IProgress<string>? progress,
        System.Threading.CancellationToken cancellationToken)
    {
        if (hostnames.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);

        var template = LoadTarTemplate(sourcePath);

        foreach (var hostname in hostnames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputFileName = BuildOutputFileName(sourcePath, existingHostname, hostname);
            var outputPath = Path.Combine(outputDirectory, outputFileName);
            progress?.Report($"Erstelle {outputFileName}...");
            CreateBackup(outputPath, template, hostname, cancellationToken);
            progress?.Report($"Fertig: {outputFileName}");
        }
    }

    public VerificationResult VerifyBackup(string sourcePath, string generatedPath)
    {
        var original = ReadArchive(sourcePath);
        var generated = ReadArchive(generatedPath);

        var differences = new List<string>();
        var systemChanged = false;

        if (original.Count != generated.Count)
        {
            differences.Add($"Eintragsanzahl unterschiedlich: Original={original.Count}, Neu={generated.Count}");
        }

        var count = Math.Min(original.Count, generated.Count);
        for (var i = 0; i < count; i++)
        {
            var originalEntry = original[i];
            var generatedEntry = generated[i];

            if (!string.Equals(originalEntry.Name, generatedEntry.Name, StringComparison.Ordinal))
            {
                differences.Add($"Eintrag {i + 1}: Name unterschiedlich (Original={originalEntry.Name}, Neu={generatedEntry.Name})");
                continue;
            }

            var isSystem = string.Equals(originalEntry.Name, SystemConfigPath, StringComparison.Ordinal);
            if (!EntriesEqual(originalEntry, generatedEntry, allowSizeDiff: isSystem, allowHashDiff: isSystem))
            {
                differences.Add(originalEntry.Name);
            }

            if (isSystem)
            {
                if (!string.Equals(originalEntry.Sha256, generatedEntry.Sha256, StringComparison.Ordinal)
                    || originalEntry.Size != generatedEntry.Size)
                {
                    systemChanged = true;
                }
            }
        }

        var isValid = differences.Count == 0;
        return new VerificationResult(isValid, systemChanged, differences);
    }

    public static string BuildOutputFileName(string sourcePath, string? existingHostname, string newHostname)
    {
        var fileName = Path.GetFileName(sourcePath);
        var baseName = fileName;

        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            baseName = fileName[..^7];
        }
        else if (fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            baseName = fileName[..^4];
        }
        else
        {
            baseName = Path.GetFileNameWithoutExtension(fileName);
        }

        string newBase;
        if (!string.IsNullOrEmpty(existingHostname)
            && baseName.Contains(existingHostname, StringComparison.Ordinal))
        {
            newBase = baseName.Replace(existingHostname, newHostname, StringComparison.Ordinal);
        }
        else
        {
            newBase = $"{baseName}-{newHostname}";
        }

        return newBase + ".tar.gz";
    }

    private static void CreateBackup(
        string outputPath,
        TarTemplate template,
        string hostname,
        System.Threading.CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath))
        {
            throw new IOException($"Die Datei '{Path.GetFileName(outputPath)}' existiert bereits.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var updatedTar = template.BuildTarWithHostname(hostname);

        using var outputStream = File.Create(outputPath);
        using var gzipOut = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: false);
        gzipOut.Write(updatedTar, 0, updatedTar.Length);
    }

    private static TarTemplate LoadTarTemplate(string sourcePath)
    {
        var tarBytes = DecompressTar(sourcePath);

        var offset = 0;
        string? pendingLongName = null;

        while (offset + TarBlockSize <= tarBytes.Length)
        {
            var header = new ReadOnlySpan<byte>(tarBytes, offset, TarBlockSize);
            if (IsAllZero(header))
            {
                break;
            }

            var typeFlag = header[TypeFlagOffset];
            var size = ParseSize(header.Slice(SizeOffset, SizeLength));
            var dataOffset = offset + TarBlockSize;
            var paddedSize = GetPaddedSize(size);

            var name = GetEntryName(header, pendingLongName);

            if (typeFlag == (byte)'L')
            {
                pendingLongName = ReadLongName(tarBytes, dataOffset, size);
            }
            else
            {
                pendingLongName = null;

                if (IsSystemConfigEntryName(name)
                    && (typeFlag == 0 || typeFlag == (byte)'0'))
                {
                    return new TarTemplate(tarBytes, offset, dataOffset, (int)size, TarBlockSize + paddedSize);
                }
            }

            offset += TarBlockSize + paddedSize;
        }

        throw new InvalidOperationException("Die Datei /etc/config/system wurde im Backup nicht gefunden.");
    }

    private static bool IsSystemConfigEntryName(string entryName)
    {
        return string.Equals(entryName.Replace('\\', '/'), SystemConfigPath, StringComparison.Ordinal);
    }

    private static byte[] DecompressTar(string sourcePath)
    {
        using var sourceStream = File.OpenRead(sourcePath);
        using var gzipIn = new GZipStream(sourceStream, CompressionMode.Decompress, leaveOpen: false);
        using var ms = new MemoryStream();
        gzipIn.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool IsAllZero(ReadOnlySpan<byte> buffer)
    {
        foreach (var b in buffer)
        {
            if (b != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetEntryName(ReadOnlySpan<byte> header, string? longName)
    {
        if (!string.IsNullOrEmpty(longName))
        {
            return longName;
        }

        var name = ReadNullTerminatedString(header.Slice(NameOffset, NameLength));
        var prefix = ReadNullTerminatedString(header.Slice(PrefixOffset, PrefixLength));
        if (!string.IsNullOrEmpty(prefix))
        {
            return $"{prefix}/{name}";
        }

        return name;
    }

    private static string ReadLongName(byte[] tarBytes, int dataOffset, long size)
    {
        var length = (int)Math.Min(size, tarBytes.Length - dataOffset);
        var name = Encoding.ASCII.GetString(tarBytes, dataOffset, length);
        var nullIndex = name.IndexOf('\0');
        return nullIndex >= 0 ? name[..nullIndex] : name;
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<byte> span)
    {
        var index = span.IndexOf((byte)0);
        var slice = index >= 0 ? span[..index] : span;
        return Encoding.ASCII.GetString(slice).Trim();
    }

    private static long ParseSize(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0)
        {
            return 0;
        }

        if ((span[0] & 0x80) != 0)
        {
            throw new NotSupportedException("Base-256 Größenangaben werden nicht unterstützt.");
        }

        var text = Encoding.ASCII.GetString(span).Trim('\0', ' ');
        return string.IsNullOrEmpty(text) ? 0 : Convert.ToInt64(text, 8);
    }

    private static int GetPaddedSize(long size)
    {
        var remainder = (int)(size % TarBlockSize);
        return remainder == 0 ? (int)size : (int)(size + (TarBlockSize - remainder));
    }

    private sealed class TarTemplate
    {
        private readonly byte[] _tarBytes;
        private readonly int _systemHeaderOffset;
        private readonly int _systemDataOffset;
        private readonly int _systemDataSize;
        private readonly int _systemTotalSize;

        public TarTemplate(byte[] tarBytes, int systemHeaderOffset, int systemDataOffset, int systemDataSize, int systemTotalSize)
        {
            _tarBytes = tarBytes;
            _systemHeaderOffset = systemHeaderOffset;
            _systemDataOffset = systemDataOffset;
            _systemDataSize = systemDataSize;
            _systemTotalSize = systemTotalSize;
        }

        public byte[] GetSystemDataBytes()
        {
            var bytes = new byte[_systemDataSize];
            Buffer.BlockCopy(_tarBytes, _systemDataOffset, bytes, 0, _systemDataSize);
            return bytes;
        }

        public byte[] BuildTarWithHostname(string hostname)
        {
            var originalSystemBytes = GetSystemDataBytes();
            var updatedSystemBytes = SystemConfigEditor.UpdateDeviceNameAndHostname(originalSystemBytes, hostname);

            if (updatedSystemBytes.Length == _systemDataSize)
            {
                var output = new byte[_tarBytes.Length];
                Buffer.BlockCopy(_tarBytes, 0, output, 0, _tarBytes.Length);
                Buffer.BlockCopy(updatedSystemBytes, 0, output, _systemDataOffset, updatedSystemBytes.Length);
                return output;
            }

            using var ms = new MemoryStream(_tarBytes.Length + (updatedSystemBytes.Length - _systemDataSize));
            ms.Write(_tarBytes, 0, _systemHeaderOffset);

            var header = new byte[TarBlockSize];
            Buffer.BlockCopy(_tarBytes, _systemHeaderOffset, header, 0, TarBlockSize);
            UpdateHeaderSizeAndChecksum(header, updatedSystemBytes.Length);
            ms.Write(header, 0, header.Length);

            ms.Write(updatedSystemBytes, 0, updatedSystemBytes.Length);
            var padding = GetPaddingLength(updatedSystemBytes.Length);
            if (padding > 0)
            {
                ms.Write(new byte[padding], 0, padding);
            }

            var remainderOffset = _systemHeaderOffset + _systemTotalSize;
            ms.Write(_tarBytes, remainderOffset, _tarBytes.Length - remainderOffset);
            return ms.ToArray();
        }

        private static int GetPaddingLength(int size)
        {
            var remainder = size % TarBlockSize;
            return remainder == 0 ? 0 : TarBlockSize - remainder;
        }

        private static void UpdateHeaderSizeAndChecksum(byte[] header, int size)
        {
            WriteOctal(header, SizeOffset, SizeLength, size);
            for (var i = ChecksumOffset; i < ChecksumOffset + ChecksumLength; i++)
            {
                header[i] = 0x20;
            }

            var checksum = 0;
            foreach (var b in header)
            {
                checksum += b;
            }

            WriteChecksum(header, checksum);
        }

        private static void WriteOctal(byte[] header, int offset, int length, int value)
        {
            var text = Convert.ToString(value, 8);
            if (text.Length > length - 1)
            {
                throw new InvalidOperationException("Die neue Dateigröße ist zu groß für das Tar-Format.");
            }

            var padded = text.PadLeft(length - 1, '0');
            for (var i = 0; i < length - 1; i++)
            {
                header[offset + i] = (byte)padded[i];
            }

            header[offset + length - 1] = 0;
        }

        private static void WriteChecksum(byte[] header, int checksum)
        {
            var text = Convert.ToString(checksum, 8).PadLeft(6, '0');
            for (var i = 0; i < 6; i++)
            {
                header[ChecksumOffset + i] = (byte)text[i];
            }

            header[ChecksumOffset + 6] = 0;
            header[ChecksumOffset + 7] = 0x20;
        }
    }

    private const int TarBlockSize = 512;
    private const int NameOffset = 0;
    private const int NameLength = 100;
    private const int SizeOffset = 124;
    private const int SizeLength = 12;
    private const int TypeFlagOffset = 156;
    private const int PrefixOffset = 345;
    private const int PrefixLength = 155;
    private const int ChecksumOffset = 148;
    private const int ChecksumLength = 8;

    private static List<ArchiveEntry> ReadArchive(string path)
    {
        var entries = new List<ArchiveEntry>();

        using var sourceStream = File.OpenRead(path);
        using var gzip = new GZipStream(sourceStream, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            var hash = entry.EntryType == TarEntryType.RegularFile && entry.DataStream != null
                ? ComputeSha256(entry.DataStream)
                : null;

            var userName = entry is PosixTarEntry posix ? posix.UserName ?? string.Empty : string.Empty;
            var groupName = entry is PosixTarEntry posix2 ? posix2.GroupName ?? string.Empty : string.Empty;

            var info = new ArchiveEntry(
                entry.Name,
                entry.EntryType,
                entry.Mode,
                entry.Uid,
                entry.Gid,
                userName,
                groupName,
                entry.ModificationTime,
                entry.Length,
                entry.LinkName ?? string.Empty,
                hash);

            entries.Add(info);
        }

        return entries;
    }

    private static string ComputeSha256(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static bool EntriesEqual(ArchiveEntry original, ArchiveEntry generated, bool allowSizeDiff, bool allowHashDiff)
    {
        if (original.EntryType != generated.EntryType)
        {
            return false;
        }

        if (original.Mode != generated.Mode
            || original.Uid != generated.Uid
            || original.Gid != generated.Gid
            || !string.Equals(original.UserName, generated.UserName, StringComparison.Ordinal)
            || !string.Equals(original.GroupName, generated.GroupName, StringComparison.Ordinal)
            || original.ModificationTime != generated.ModificationTime
            || !string.Equals(original.LinkName, generated.LinkName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!allowSizeDiff && original.Size != generated.Size)
        {
            return false;
        }

        if (!allowHashDiff && !string.Equals(original.Sha256, generated.Sha256, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public sealed record VerificationResult(bool IsValid, bool SystemChanged, IReadOnlyList<string> Differences);

    private sealed record ArchiveEntry(
        string Name,
        TarEntryType EntryType,
        System.IO.UnixFileMode Mode,
        int Uid,
        int Gid,
        string UserName,
        string GroupName,
        DateTimeOffset ModificationTime,
        long Size,
        string LinkName,
        string? Sha256);
}
