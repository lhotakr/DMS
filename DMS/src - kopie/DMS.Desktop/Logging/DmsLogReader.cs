using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;

namespace DMS.Desktop.Logging;

public sealed class DmsLogReader
{
    private static readonly string[] TimestampFormats =
    {
        "dd.MM.yyyy HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss.fff"
    };

    public IReadOnlyList<DmsLogEntry> ReadDay(
        string logsRootPath,
        DateTime day)
    {
        var fileNames = new[]
        {
            Path.Combine(logsRootPath, $"dms-{day:yyyy-MM-dd}.log"),
            Path.Combine(logsRootPath, $"dms-{day:dd.MM.yyyy}.log")
        };

        var filePath = fileNames.FirstOrDefault(File.Exists);

        if (filePath is null)
        {
            return Array.Empty<DmsLogEntry>();
        }

        var result = new List<DmsLogEntry>();

        foreach (var line in File.ReadLines(filePath))
        {
            if (TryParseLine(line, out var entry))
            {
                result.Add(entry);
            }
        }

        return result
            .OrderByDescending(item => item.Timestamp)
            .ToList();
    }

    private static bool TryParseLine(
        string line,
        out DmsLogEntry entry)
    {
        entry = new DmsLogEntry
        {
            RawLine = line
        };

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(" | ", 3, StringSplitOptions.None);

        if (parts.Length < 3)
        {
            entry.Message = NormalizeLegacyTechnicalMessage(line);
            return true;
        }

        if (!TryParseTimestamp(parts[0].Trim(), out var timestamp))
        {
            entry.Message = NormalizeLegacyTechnicalMessage(line);
            return true;
        }

        entry.Timestamp = timestamp;
        entry.Level = parts[1].Trim();

        var message = parts[2].Trim();
        entry.Message = NormalizeLegacyTechnicalMessage(message);

        var values = ParseKeyValues(message);

        entry.OperationId = Get(values, "OperationId");
        entry.CorrelationId = Get(values, "CorrelationId");
        if (string.IsNullOrWhiteSpace(entry.CorrelationId))
        {
            entry.CorrelationId = entry.OperationId;
        }

        entry.Module = Get(values, "Module");
        entry.PersonId = Get(values, "PersonId");
        entry.Code = Get(values, "Code");
        entry.Text = Get(values, "Text");
        entry.Area = Get(values, "Area");
        entry.Action = Get(values, "Action");
        entry.Entity = Get(values, "Entity");
        entry.EntityId = Get(values, "EntityId");
        entry.Field = Get(values, "Field");
        entry.OldValue = Get(values, "Old");
        entry.NewValue = Get(values, "New");
        entry.User = Get(values, "User");
        entry.Roles = Get(values, "Roles");
        entry.DurationMs = Get(values, "DurationMs");
        entry.Reason = NormalizeLegacyTechnicalMessage(Get(values, "Reason"));
        entry.File = Get(values, "File");
        entry.Detail = Get(values, "Detail");

        if (string.IsNullOrWhiteSpace(entry.User))
        {
            entry.User = TryExtractUserFromLegacyMessage(message);
        }

        return true;
    }

    private static bool TryParseTimestamp(string value, out DateTime timestamp)
    {
        if (DateTime.TryParseExact(
                value,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp))
        {
            return true;
        }

        return DateTime.TryParseExact(
            value,
            TimestampFormats,
            CultureInfo.GetCultureInfo("cs-CZ"),
            DateTimeStyles.None,
            out timestamp);
    }

    private static Dictionary<string, string> ParseKeyValues(string message)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var segments = message.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var index = segment.IndexOf('=');

            if (index <= 0)
            {
                continue;
            }

            var key = segment[..index].Trim();
            var value = segment[(index + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out var value)
            ? value
            : string.Empty;
    }

    private static string TryExtractUserFromLegacyMessage(string message)
    {
        const string czechPrefix = "Uživatel:";
        const string englishPrefix = "User:";

        var czechUser = ExtractValueAfterPrefix(message, czechPrefix);

        if (!string.IsNullOrWhiteSpace(czechUser))
        {
            return czechUser;
        }

        return ExtractValueAfterPrefix(message, englishPrefix);
    }

    private static string ExtractValueAfterPrefix(string message, string prefix)
    {
        var index = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return string.Empty;
        }

        var afterPrefix = message[(index + prefix.Length)..];
        var endIndex = afterPrefix.IndexOf(';');

        return endIndex >= 0
            ? afterPrefix[..endIndex].Trim()
            : afterPrefix.Trim();
    }

    private static string NormalizeLegacyTechnicalMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var value = message.Trim();

        const string unknownTransactionPrefix = "Neznámá transakce:";

        if (value.StartsWith(unknownTransactionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var transactionCode = value[unknownTransactionPrefix.Length..].Trim();
            return $"Unknown transaction: {transactionCode}";
        }

        const string noTransactionMessage = "Nebyla zadána žádná transakce.";

        if (value.Equals(noTransactionMessage, StringComparison.OrdinalIgnoreCase))
        {
            return "No transaction was entered.";
        }

        const string transactionErrorPrefix = "Chyba transakce ";

        if (value.StartsWith(transactionErrorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = value[transactionErrorPrefix.Length..].Trim();
            var separatorIndex = rest.IndexOf(':');

            if (separatorIndex > 0)
            {
                var code = rest[..separatorIndex].Trim();
                var reason = NormalizeLegacyTechnicalMessage(rest[(separatorIndex + 1)..].Trim());
                return $"Transaction error {code}: {reason}";
            }

            return $"Transaction error {rest}";
        }

        const string transactionDeniedPrefix = "Zamítnuté spuštění transakce ";

        if (value.StartsWith(transactionDeniedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = value[transactionDeniedPrefix.Length..].Trim();
            var separatorIndex = rest.IndexOf(':');

            if (separatorIndex > 0)
            {
                var code = rest[..separatorIndex].Trim();
                var reason = NormalizeLegacyTechnicalMessage(rest[(separatorIndex + 1)..].Trim());
                return $"Transaction execution denied {code}: {reason}";
            }

            return $"Transaction execution denied {rest}";
        }

        const string clientStarted = "DMS klient spuštěn.";

        if (value.Equals(clientStarted, StringComparison.OrdinalIgnoreCase))
        {
            return "DMS client started.";
        }

        return value;
    }
}
