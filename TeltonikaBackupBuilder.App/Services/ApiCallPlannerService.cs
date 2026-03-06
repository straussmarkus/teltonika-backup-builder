using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TeltonikaBackupBuilder.App.Services;

public sealed class ApiCallPlannerService
{
    private const string ConfigRootPrefix = "etc/config/";

    public IReadOnlyList<PlannedApiCall> CreatePlan(BackupConfigAnalysis analysis)
    {
        if (analysis.ConfigFiles.Count == 0)
        {
            throw new InvalidOperationException("Es wurden keine Konfigurationsdateien fuer den API-Plan gefunden.");
        }

        var calls = new List<PlannedApiCall>();
        var order = 1;

        foreach (var file in analysis.ConfigFiles)
        {
            if (!file.ArchivePath.StartsWith(ConfigRootPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var configName = file.ArchivePath[ConfigRootPrefix.Length..];
            if (string.IsNullOrWhiteSpace(configName))
            {
                continue;
            }

            var sections = ParseUciSections(file.Content);
            if (sections.Count == 0)
            {
                continue;
            }

            var requestBody = BuildConfigPutBody(sections);
            calls.Add(new PlannedApiCall(
                order++,
                $"Setze Konfiguration: {configName}",
                "PUT",
                $"/api/{configName}/config",
                ApiRequestKind.Json,
                requestBody,
                ApiCallKind.ApplyConfigFile));
        }

        calls.Add(new PlannedApiCall(
            order++,
            "Router-Backup generieren",
            "POST",
            "/api/backup/actions/generate",
            ApiRequestKind.Json,
            BuildEncryptPayload(),
            ApiCallKind.GenerateBackup));

        calls.Add(new PlannedApiCall(
            order,
            "Generiertes Backup herunterladen",
            "POST",
            "/api/backup/actions/download",
            ApiRequestKind.None,
            RequestBody: null,
            ApiCallKind.DownloadBackup,
            ExpectsBinaryResponse: true));

        return calls;
    }

    private static string BuildEncryptPayload()
    {
        var payload = new
        {
            data = new
            {
                encrypt = "0"
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildConfigPutBody(IReadOnlyList<UciSection> sections)
    {
        var data = new List<Dictionary<string, object?>>(sections.Count);
        foreach (var section in sections)
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(section.Name))
            {
                obj["id"] = section.Name;
            }

            foreach (var (key, value) in section.Options)
            {
                obj[key] = value;
            }

            foreach (var (key, values) in section.Lists)
            {
                obj[key] = values;
            }

            data.Add(obj);
        }

        var payload = new Dictionary<string, object?>
        {
            ["data"] = data
        };

        return JsonSerializer.Serialize(payload);
    }

    private static List<UciSection> ParseUciSections(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        var sections = new List<UciSection>();
        UciSection? current = null;

        foreach (var rawLine in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("config ", StringComparison.Ordinal))
            {
                var parts = SplitUciParts(line);
                if (parts.Count >= 2)
                {
                    current = new UciSection(parts[1], parts.Count >= 3 ? parts[2] : null);
                    sections.Add(current);
                }

                continue;
            }

            if (current == null)
            {
                continue;
            }

            if (line.StartsWith("option ", StringComparison.Ordinal))
            {
                var parts = SplitUciParts(line);
                if (parts.Count >= 3)
                {
                    current.Options[parts[1]] = parts[2];
                }

                continue;
            }

            if (line.StartsWith("list ", StringComparison.Ordinal))
            {
                var parts = SplitUciParts(line);
                if (parts.Count >= 3)
                {
                    if (!current.Lists.TryGetValue(parts[1], out var values))
                    {
                        values = new List<string>();
                        current.Lists[parts[1]] = values;
                    }

                    values.Add(parts[2]);
                }
            }
        }

        return sections;
    }

    private static List<string> SplitUciParts(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (!inDouble && c == '\'')
            {
                inSingle = !inSingle;
                continue;
            }

            if (!inSingle && c == '"')
            {
                inDouble = !inDouble;
                continue;
            }

            if (!inSingle && !inDouble && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private sealed class UciSection
    {
        public UciSection(string type, string? name)
        {
            Type = type;
            Name = name;
            Options = new Dictionary<string, string>(StringComparer.Ordinal);
            Lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        public string Type { get; }

        public string? Name { get; }

        public Dictionary<string, string> Options { get; }

        public Dictionary<string, List<string>> Lists { get; }
    }
}
