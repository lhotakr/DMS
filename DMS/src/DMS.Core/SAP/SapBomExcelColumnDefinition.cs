namespace DMS.Core.Sap;

public sealed class SapBomExcelColumnDefinition
{
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string SapMeaning { get; init; } = string.Empty;
    public string DmsMeaning { get; init; } = string.Empty;
}

public static class SapBomExcelColumnDefinitions
{
    public static IReadOnlyList<SapBomExcelColumnDefinition> All { get; } =
        new List<SapBomExcelColumnDefinition>
        {
            new() { TableName = "MAST", ColumnName = "MATNR", IsRequired = true, SapMeaning = "Číslo materiálu", DmsMeaning = "Materiál nebo artikl, ke kterému je přiřazen kusovník." },
            new() { TableName = "MAST", ColumnName = "WERKS", IsRequired = true, SapMeaning = "Závod", DmsMeaning = "9200 = intercompany / mateřský kusovník, 2000 = lokální dekorační kusovník." },
            new() { TableName = "MAST", ColumnName = "STLAN", IsRequired = true, SapMeaning = "Použití kusovníku", DmsMeaning = "Použití BOM. Slouží k párování hlavičky kusovníku." },
            new() { TableName = "MAST", ColumnName = "STLNR", IsRequired = true, SapMeaning = "Číslo kusovníku", DmsMeaning = "Hlavní klíč pro spojení MAST, STKO a STPO." },
            new() { TableName = "MAST", ColumnName = "STLAL", IsRequired = true, SapMeaning = "Alternativa kusovníku", DmsMeaning = "Alternativa BOM." },

            new() { TableName = "STKO", ColumnName = "STLNR", IsRequired = true, SapMeaning = "Číslo kusovníku", DmsMeaning = "Klíč hlavičky kusovníku." },
            new() { TableName = "STKO", ColumnName = "STLAL", IsRequired = true, SapMeaning = "Alternativa kusovníku", DmsMeaning = "Alternativa BOM." },
            new() { TableName = "STKO", ColumnName = "STLAN", IsRequired = true, SapMeaning = "Použití kusovníku", DmsMeaning = "Použití BOM." },
            new() { TableName = "STKO", ColumnName = "BMENG", IsRequired = true, SapMeaning = "Základní množství", DmsMeaning = "Na 9200 důležité jako množství na paletě." },
            new() { TableName = "STKO", ColumnName = "BMEIN", IsRequired = true, SapMeaning = "Základní měrná jednotka", DmsMeaning = "Jednotka základního množství." },

            new() { TableName = "STAS", ColumnName = "STLNR", IsRequired = true, SapMeaning = "Číslo kusovníku", DmsMeaning = "Spojení STAS se STKO a STPO." },
            new() { TableName = "STAS", ColumnName = "STLAL", IsRequired = true, SapMeaning = "Alternativa kusovníku", DmsMeaning = "Určuje, ke které alternativě patří uzel položky." },
            new() { TableName = "STAS", ColumnName = "STLKN", IsRequired = true, SapMeaning = "Uzel položky kusovníku", DmsMeaning = "Spojení na STPO-STLKN." },
            new() { TableName = "STAS", ColumnName = "STASZ", IsRequired = false, SapMeaning = "Čítač", DmsMeaning = "Pomocný čítač vazby položky." },
            new() { TableName = "STAS", ColumnName = "LOEKZ", IsRequired = false, SapMeaning = "Příznak smazání", DmsMeaning = "Smazané vazby položek se ignorují." },

            new() { TableName = "STPO", ColumnName = "STLNR", IsRequired = true, SapMeaning = "Číslo kusovníku", DmsMeaning = "Párování položky na kusovník." },
            new() { TableName = "STPO", ColumnName = "POSNR", IsRequired = true, SapMeaning = "Číslo položky", DmsMeaning = "Pozice komponenty v kusovníku." },
            new() { TableName = "STPO", ColumnName = "IDNRK", IsRequired = true, SapMeaning = "Komponenta", DmsMeaning = "Číslo komponenty nebo materiálu položky." },
            new() { TableName = "STPO", ColumnName = "MENGE", IsRequired = true, SapMeaning = "Množství komponenty", DmsMeaning = "Množství položky v kusovníku." },
            new() { TableName = "STPO", ColumnName = "MEINS", IsRequired = true, SapMeaning = "Měrná jednotka", DmsMeaning = "Jednotka množství položky." },
            new() { TableName = "STPO", ColumnName = "POSTP", IsRequired = true, SapMeaning = "Typ položky", DmsMeaning = "Pomáhá rozlišit skladovou, textovou nebo jinou položku." },
            new() { TableName = "STPO", ColumnName = "STLKN", IsRequired = true, SapMeaning = "Uzel položky kusovníku", DmsMeaning = "Spojení položky STPO s alternativou přes STAS." },
            new() { TableName = "STPO", ColumnName = "POTX1", IsRequired = false, SapMeaning = "Text položky 1", DmsMeaning = "Volitelné. Užitečné pro textové položky, například síta." },
            new() { TableName = "STPO", ColumnName = "POTX2", IsRequired = false, SapMeaning = "Text položky 2", DmsMeaning = "Volitelné. Doplňkový text položky." },
            new() { TableName = "STPO", ColumnName = "AUSCH", IsRequired = false, SapMeaning = "Odpad komponenty v %", DmsMeaning = "Na závodě 9200 se používá pro porovnání se zmetkovitostí poslední operace ZPP2 na závodě 2000." },
            new() { TableName = "STPO", ColumnName = "FMENG", IsRequired = false, SapMeaning = "Pevné množství", DmsMeaning = "U přípravků a nástrojů 23* musí být nastaveno pevné množství." },
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