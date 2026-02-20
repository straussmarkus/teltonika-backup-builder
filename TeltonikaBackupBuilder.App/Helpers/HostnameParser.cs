using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TeltonikaBackupBuilder.App.Helpers;

public static class HostnameParser
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static bool TryParse(string input, out List<string> hostnames, out string? error)
    {
        hostnames = new List<string>();
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Bitte mindestens einen Hostnamen angeben.";
            return false;
        }

        var tokens = Regex.Split(input, @"[\s,;]+")
            .Select(t => t.Trim())
            .Where(t => t.Length > 0);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            if (!IsValidHostname(token, out var reason))
            {
                error = $"Ungültiger Hostname '{token}': {reason}";
                return false;
            }

            if (!seen.Add(token))
            {
                error = $"Der Hostname '{token}' ist doppelt vorhanden.";
                return false;
            }

            hostnames.Add(token);
        }

        if (hostnames.Count == 0)
        {
            error = "Bitte mindestens einen Hostnamen angeben.";
            return false;
        }

        return true;
    }

    private static bool IsValidHostname(string value, out string reason)
    {
        reason = string.Empty;

        if (value.Any(char.IsWhiteSpace))
        {
            reason = "Darf keine Leerzeichen enthalten.";
            return false;
        }

        if (value.IndexOf('"') >= 0 || value.IndexOf('\'') >= 0)
        {
            reason = "Darf keine Anführungszeichen enthalten.";
            return false;
        }

        if (value.IndexOfAny(InvalidFileNameChars) >= 0)
        {
            reason = "Enthält ungültige Zeichen für Dateinamen.";
            return false;
        }

        if (value.Any(ch => ch > 0x7F))
        {
            reason = "Darf nur ASCII-Zeichen enthalten.";
            return false;
        }

        if (value is "." or "..")
        {
            reason = "Nicht erlaubt.";
            return false;
        }

        return true;
    }
}
