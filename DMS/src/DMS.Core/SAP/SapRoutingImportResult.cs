namespace DMS.Core.Sap;

public sealed class SapRoutingImportResult
{
    public int MaplRows { get; set; }
    public int PlkoRows { get; set; }
    public int PlpoRows { get; set; }

    public int ImportedRoutingCount { get; set; }
    public int ImportedOperationCount { get; set; }

    public int SkippedAlternativeCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorRows { get; set; }

    public List<string> Messages { get; set; } = new();
}