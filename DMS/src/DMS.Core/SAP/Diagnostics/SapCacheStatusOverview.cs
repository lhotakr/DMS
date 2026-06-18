namespace DMS.Core.Sap.Diagnostics;

public sealed class SapCacheStatusOverview
{
    public string BasePath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public List<SapCacheStatusRow> Rows { get; init; } = new();

    public int ExistingFiles => Rows.Count(row => row.Exists);
    public int MissingFiles => Rows.Count(row => !row.Exists);
}