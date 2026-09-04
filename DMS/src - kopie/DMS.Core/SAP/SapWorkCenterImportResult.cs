namespace DMS.Core.Sap;

public sealed class SapWorkCenterImportResult
{
    public int CrhdRows { get; set; }
    public int CrtxRows { get; set; }

    public int ImportedWorkCenterCount { get; set; }
    public int ImportedTextCount { get; set; }

    public int ErrorRows { get; set; }

    public List<string> Messages { get; set; } = new();
}