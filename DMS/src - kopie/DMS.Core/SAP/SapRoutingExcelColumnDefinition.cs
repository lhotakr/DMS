namespace DMS.Core.Sap;

public sealed class SapRoutingExcelColumnDefinition
{
    public string TableName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string SapMeaning { get; init; } = string.Empty;
    public string DmsMeaning { get; init; } = string.Empty;
}

public static class SapRoutingExcelColumnDefinitions
{
    public static IReadOnlyList<SapRoutingExcelColumnDefinition> All { get; } =
        new List<SapRoutingExcelColumnDefinition>
        {
            new() { TableName = "MAPL", ColumnName = "MATNR", IsRequired = true, SapMeaning = "Číslo materiálu", DmsMeaning = "Materiál/artikl, ke kterému je přiřazen pracovní postup." },
            new() { TableName = "MAPL", ColumnName = "WERKS", IsRequired = true, SapMeaning = "Závod", DmsMeaning = "9200 = intercompany/skupinový postup, 2000 = lokální konkrétní postup." },
            new() { TableName = "MAPL", ColumnName = "PLNTY", IsRequired = true, SapMeaning = "Typ plánu", DmsMeaning = "Typ task listu." },
            new() { TableName = "MAPL", ColumnName = "PLNNR", IsRequired = true, SapMeaning = "Skupina plánu", DmsMeaning = "Číslo skupiny pracovního postupu." },
            new() { TableName = "MAPL", ColumnName = "PLNAL", IsRequired = true, SapMeaning = "Alternativa", DmsMeaning = "Pro DMS používáme standardně pouze PLNAL = 1." },

            new() { TableName = "PLKO", ColumnName = "PLNTY", IsRequired = true, SapMeaning = "Typ plánu", DmsMeaning = "Párování na MAPL/PLPO." },
            new() { TableName = "PLKO", ColumnName = "PLNNR", IsRequired = true, SapMeaning = "Skupina plánu", DmsMeaning = "Párování hlavičky pracovního postupu." },
            new() { TableName = "PLKO", ColumnName = "PLNAL", IsRequired = true, SapMeaning = "Alternativa", DmsMeaning = "Používáme PLNAL = 1." },
            new() { TableName = "PLKO", ColumnName = "KTEXT", IsRequired = false, SapMeaning = "Text hlavičky", DmsMeaning = "Popis pracovního postupu." },
            new() { TableName = "PLKO", ColumnName = "STATU", IsRequired = false, SapMeaning = "Status", DmsMeaning = "Stav pracovního postupu." },
            new() { TableName = "PLKO", ColumnName = "VERWE", IsRequired = false, SapMeaning = "Použití", DmsMeaning = "Použití pracovního postupu." },

            new() { TableName = "PLPO", ColumnName = "PLNTY", IsRequired = true, SapMeaning = "Typ plánu", DmsMeaning = "Párování operace." },
            new() { TableName = "PLPO", ColumnName = "PLNNR", IsRequired = true, SapMeaning = "Skupina plánu", DmsMeaning = "Párování operace na postup." },
            new() { TableName = "PLPO", ColumnName = "VORNR", IsRequired = true, SapMeaning = "Číslo operace", DmsMeaning = "Pořadí operace." },
            new() { TableName = "PLPO", ColumnName = "ARBID", IsRequired = true, SapMeaning = "Interní ID pracoviště", DmsMeaning = "Párování na CRHD-OBJID." },
            new() { TableName = "PLPO", ColumnName = "STEUS", IsRequired = true, SapMeaning = "Řídicí klíč", DmsMeaning = "Kontrola ZPP1/ZPP2/ZPP5." },
            new() { TableName = "PLPO", ColumnName = "LTXA1", IsRequired = false, SapMeaning = "Krátký text operace", DmsMeaning = "Popis operace." },
            new() { TableName = "PLPO", ColumnName = "BMSCH", IsRequired = true, SapMeaning = "Základní množství", DmsMeaning = "Společně s VGW03 určuje takt stroje." },
            new() { TableName = "PLPO", ColumnName = "MEINH", IsRequired = false, SapMeaning = "Jednotka základního množství", DmsMeaning = "Jednotka pro BMSCH." },
            new() { TableName = "PLPO", ColumnName = "VGW01", IsRequired = true, SapMeaning = "Standardní hodnota 1", DmsMeaning = "Používaný čas/norma dle lokální SAP logiky." },
            new() { TableName = "PLPO", ColumnName = "VGE01", IsRequired = false, SapMeaning = "Jednotka VGW01", DmsMeaning = "Jednotka hodnoty VGW01." },
            new() { TableName = "PLPO", ColumnName = "VGW03", IsRequired = true, SapMeaning = "Standardní hodnota 3", DmsMeaning = "U vás typicky 1; s BMSCH definuje takt stroje." },
            new() { TableName = "PLPO", ColumnName = "VGE03", IsRequired = false, SapMeaning = "Jednotka VGW03", DmsMeaning = "Jednotka hodnoty VGW03." },
            new() { TableName = "PLPO", ColumnName = "VGW04", IsRequired = true, SapMeaning = "Standardní hodnota 4", DmsMeaning = "Na závodě 2000 počet lidí / operátorů." },
            new() { TableName = "PLPO", ColumnName = "VGE04", IsRequired = false, SapMeaning = "Jednotka VGW04", DmsMeaning = "Jednotka VGW04." },
            new() { TableName = "PLPO", ColumnName = "INFNR", IsRequired = false, SapMeaning = "Info record", DmsMeaning = "Na 9200 kritické; první operace ZPP5 musí mít INFNR." },
            new() { TableName = "PLPO", ColumnName = "AUSSS", IsRequired = false, SapMeaning = "Odpad operace v %", DmsMeaning = "Na závodě 2000 má být odpad zadaný pouze na poslední operaci s řídicím klíčem ZPP2." },

            new() { TableName = "PLAS", ColumnName = "PLNTY", IsRequired = true, SapMeaning = "Typ pracovního postupu", DmsMeaning = "Spojení PLAS s PLKO a PLPO." },
            new() { TableName = "PLAS", ColumnName = "PLNNR", IsRequired = true, SapMeaning = "Skupina pracovního postupu", DmsMeaning = "Spojení PLAS s PLKO a PLPO." },
            new() { TableName = "PLAS", ColumnName = "PLNAL", IsRequired = true, SapMeaning = "Alternativa pracovního postupu", DmsMeaning = "Určuje, ke které alternativě patří uzel operace." },
            new() { TableName = "PLAS", ColumnName = "PLNKN", IsRequired = true, SapMeaning = "Uzel operace", DmsMeaning = "Spojení na PLPO-PLNKN." },
            new() { TableName = "PLAS", ColumnName = "ZAEHL", IsRequired = false, SapMeaning = "Čítač", DmsMeaning = "Doplňkové spojení na PLPO-ZAEHL." },
            new() { TableName = "PLAS", ColumnName = "LOEKZ", IsRequired = false, SapMeaning = "Příznak smazání", DmsMeaning = "Smazané vazby operací se ignorují." },

            new() { TableName = "CRHD", ColumnName = "OBJID", IsRequired = true, SapMeaning = "Objektové ID pracoviště", DmsMeaning = "Párování s PLPO-ARBID." },
            new() { TableName = "CRHD", ColumnName = "ARBPL", IsRequired = true, SapMeaning = "Pracoviště", DmsMeaning = "Skutečný kód pracoviště zobrazovaný v DMS." },
            new() { TableName = "CRHD", ColumnName = "WERKS", IsRequired = false, SapMeaning = "Závod", DmsMeaning = "Závod pracoviště." },

            new() { TableName = "CRTX", ColumnName = "OBJID", IsRequired = true, SapMeaning = "Objektové ID pracoviště", DmsMeaning = "Párování na CRHD." },
            new() { TableName = "CRTX", ColumnName = "SPRAS", IsRequired = false, SapMeaning = "Jazyk", DmsMeaning = "Jazyk textu pracoviště." },
            new() { TableName = "CRTX", ColumnName = "KTEXT", IsRequired = false, SapMeaning = "Text pracoviště", DmsMeaning = "Popis pracoviště." }
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