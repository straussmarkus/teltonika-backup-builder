using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TeltonikaBackupBuilder.App.Services;

public static class SystemConfigEditor
{
    public static byte[] UpdateDeviceNameAndHostname(byte[] originalBytes, string hostname)
    {
        var output = new System.IO.MemoryStream(originalBytes.Length + hostname.Length * 2);
        var updatedDevice = false;
        var updatedHost = false;

        var index = 0;
        while (index < originalBytes.Length)
        {
            var lineEnd = Array.IndexOf(originalBytes, (byte)'\n', index);
            var hasNewLine = lineEnd != -1;
            if (!hasNewLine)
            {
                lineEnd = originalBytes.Length;
            }

            var lineLength = lineEnd - index;
            var newlineLength = 0;
            if (hasNewLine)
            {
                newlineLength = 1;
                if (lineLength > 0 && originalBytes[lineEnd - 1] == '\r')
                {
                    lineLength -= 1;
                    newlineLength = 2;
                }
            }

            var lineSpan = new ReadOnlySpan<byte>(originalBytes, index, lineLength);
            if (TryUpdateLine(lineSpan, hostname, out var updatedLine, out var optionKey))
            {
                output.Write(updatedLine, 0, updatedLine.Length);
                if (optionKey == "devicename")
                {
                    updatedDevice = true;
                }
                else if (optionKey == "hostname")
                {
                    updatedHost = true;
                }
            }
            else
            {
                output.Write(lineSpan);
            }

            if (newlineLength > 0)
            {
                var newlineStart = index + lineLength;
                output.Write(originalBytes, newlineStart, newlineLength);
            }

            if (!hasNewLine)
            {
                break;
            }

            index = lineEnd + 1;
        }

        if (!updatedDevice || !updatedHost)
        {
            throw new InvalidOperationException("Die Optionen 'devicename' und/oder 'hostname' wurden in /etc/config/system nicht gefunden.");
        }

        return output.ToArray();
    }

    public static string? TryGetOptionValue(byte[] originalBytes, string optionName)
    {
        var ascii = Encoding.ASCII.GetString(originalBytes);
        var pattern = $@"^\s*option\s+{Regex.Escape(optionName)}\s+'([^']*)'";
        var match = Regex.Match(ascii, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool TryUpdateLine(ReadOnlySpan<byte> line, string hostname, out byte[] updatedLine, out string? optionKey)
    {
        updatedLine = Array.Empty<byte>();
        optionKey = null;

        if (line.Length == 0)
        {
            return false;
        }

        var ascii = Encoding.ASCII.GetString(line);
        var trimmed = ascii.TrimStart();
        if (trimmed.StartsWith("option devicename", StringComparison.Ordinal))
        {
            optionKey = "devicename";
        }
        else if (trimmed.StartsWith("option hostname", StringComparison.Ordinal))
        {
            optionKey = "hostname";
        }
        else
        {
            return false;
        }

        var firstQuote = line.IndexOf((byte)'\'');
        var lastQuote = line.LastIndexOf((byte)'\'');
        if (firstQuote < 0 || lastQuote <= firstQuote)
        {
            return false;
        }

        var prefix = line[..(firstQuote + 1)];
        var suffix = line[lastQuote..];
        var hostBytes = Encoding.ASCII.GetBytes(hostname);
        updatedLine = new byte[prefix.Length + hostBytes.Length + suffix.Length];
        prefix.CopyTo(updatedLine);
        hostBytes.CopyTo(updatedLine.AsSpan(prefix.Length));
        suffix.CopyTo(updatedLine.AsSpan(prefix.Length + hostBytes.Length));
        return true;
    }
}
