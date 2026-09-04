namespace DMS.Core.Sap.Diagnostics;

public sealed class SapCacheStatusRow
{
    public string Area { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public string LastChangedText { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;

    public bool Exists { get; init; }
    public int? Count { get; init; }
}