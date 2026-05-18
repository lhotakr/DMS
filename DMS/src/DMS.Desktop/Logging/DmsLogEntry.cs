namespace DMS.Desktop.Logging;

public sealed class DmsLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;

    public string TimestampText => Timestamp.ToString("dd.MM.yyyy HH:mm:ss.fff");

    public string DisplayText =>
        $"{TimestampText} | {Level} | {Message}";
}