using System.Globalization;
using System.IO;

namespace DMS.Desktop.Logging;

public sealed class DmsLogReader
{
    public IReadOnlyList<DmsLogEntry> ReadDay(string logsRootPath, DateTime day)
    {
        var filePath = Path.Combine(
            logsRootPath,
            $"dms-{day:yyyy-MM-dd}.log");

        if (!File.Exists(filePath))
        {
            return Array.Empty<DmsLogEntry>();
        }

        var entries = new List<DmsLogEntry>();

        foreach (var line in File.ReadLines(filePath))
        {
            var entry = TryParseLine(line);

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static DmsLogEntry? TryParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = line.Split(" | ", 3, StringSplitOptions.None);

        if (parts.Length < 3)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                parts[0],
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timestamp))
        {
            return null;
        }

        var level = parts[1].Trim();
        var message = parts[2].Trim();

        return new DmsLogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Message = message,
            User = ExtractUser(message)
        };
    }

    private static string ExtractUser(string message)
    {
        const string userToken = "Uživatel:";

        var index = message.IndexOf(userToken, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return string.Empty;
        }

        var start = index + userToken.Length;
        var rest = message[start..].Trim();

        var semicolonIndex = rest.IndexOf(';');

        if (semicolonIndex >= 0)
        {
            return rest[..semicolonIndex].Trim();
        }

        return rest.Trim();
    }
}