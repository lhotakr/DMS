namespace DMS.Core.Sap;

public sealed class SapMaterialImportResult
{
    public int MaraRows { get; set; }
    public int MaktRows { get; set; }
    public int JoinedRows { get; set; }
    public int ImportedRows { get; set; }
    public int IgnoredRows { get; set; }
    public int ErrorRows { get; set; }

    public List<string> Messages { get; } = new();

    public string ToDisplayText()
    {
        return
            $"Import SAP dat dokončen.\n\n" +
            $"MARA řádků: {MaraRows}\n" +
            $"MAKT řádků: {MaktRows}\n" +
            $"Spojeno: {JoinedRows}\n" +
            $"Importováno: {ImportedRows}\n" +
            $"Ignorováno: {IgnoredRows}\n" +
            $"Chyby: {ErrorRows}\n\n" +
            string.Join("\n", Messages.Take(30));
    }
}