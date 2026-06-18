namespace DMS.Core.Sap;

public sealed class SapWorkCenterExcelColumnDefinition
{
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string SapMeaning { get; init; } = string.Empty;
    public string DmsMeaning { get; init; } = string.Empty;
}

public static class SapWorkCenterExcelColumnDefinitions
{
    public static IReadOnlyList<SapWorkCenterExcelColumnDefinition> All { get; } =
        new List<SapWorkCenterExcelColumnDefinition>
        {
            new()
            {
                TableName = "CRHD",
                ColumnName = "OBJID",
                IsRequired = true,
                SapMeaning = "Object ID pracoviště",
                DmsMeaning = "Klíč pro spojení s PLPO-ARBID."
            },
            new()
            {
                TableName = "CRHD",
                ColumnName = "ARBPL",
                IsRequired = true,
                SapMeaning = "Pracoviště",
                DmsMeaning = "Kód pracoviště zobrazovaný v DMS."
            },
            new()
            {
                TableName = "CRHD",
                ColumnName = "WERKS",
                IsRequired = false,
                SapMeaning = "Závod",
                DmsMeaning = "Závod pracoviště, například 2000 nebo 9200."
            },

            new()
            {
                TableName = "CRTX",
                ColumnName = "OBJID",
                IsRequired = true,
                SapMeaning = "Object ID pracoviště",
                DmsMeaning = "Klíč pro spojení s CRHD-OBJID."
            },
            new()
            {
                TableName = "CRTX",
                ColumnName = "SPRAS",
                IsRequired = false,
                SapMeaning = "Jazyk",
                DmsMeaning = "Jazyk textu pracoviště."
            },
            new()
            {
                TableName = "CRTX",
                ColumnName = "KTEXT",
                IsRequired = false,
                SapMeaning = "Text pracoviště",
                DmsMeaning = "Popis pracoviště."
            }
        };

    public static IReadOnlyList<string> RequiredColumnsForTable(string tableName)
    {
        return All
            .Where(item => string.Equals(item.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.IsRequired)
            .Select(item => item.ColumnName)
            .ToList();
    }
}