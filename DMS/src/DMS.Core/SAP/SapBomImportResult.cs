namespace DMS.Core.Sap;

public sealed class SapBomImportResult
{
    public int MastRows { get; set; }
    public int StkoRows { get; set; }
    public int StpoRows { get; set; }

    public int ImportedBomCount { get; set; }
    public int ImportedItemCount { get; set; }

    public int ErrorRows { get; set; }

    public List<string> Messages { get; set; } = new();
}