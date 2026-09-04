using ClosedXML.Excel;
using System.Globalization;

namespace DMS.Core.Sap;

public sealed class SapBomExcelImportService
{
    private readonly JsonSapBomRepository _repository;

    public SapBomExcelImportService(JsonSapBomRepository repository)
    {
        _repository = repository;
    }

    public SapBomImportResult Import(
    string mastFilePath,
    string stkoFilePath,
    string stasFilePath,
    string stpoFilePath)
    {
        var result = new SapBomImportResult();

        var mastRows = LoadMastRows(mastFilePath);
        var stkoRows = LoadStkoRows(stkoFilePath);
        var stasRows = LoadStasRows(stasFilePath);
        var stpoRows = LoadStpoRows(stpoFilePath);

        result.MastRows = mastRows.Count;
        result.StkoRows = stkoRows.Count;
        result.StpoRows = stpoRows.Count;

        var stkoByKey = stkoRows
            .GroupBy(item => BuildBomHeaderKey(
                item.BomNumber,
                item.Alternative))
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var boms = new List<SapBom>();

        foreach (var mast in mastRows)
        {
            try
            {
                var normalizedAlternative = NormalizeAlternative(mast.Alternative);

                var headerKey = BuildBomHeaderKey(
                    mast.BomNumber,
                    normalizedAlternative);

                stkoByKey.TryGetValue(headerKey, out var stko);

                if (stko is null)
                {
                    result.Messages.Add(
                        $"MAST MATNR {mast.MaterialNumber}, STLNR {mast.BomNumber}, STLAL {mast.Alternative}: nebyla nalezena hlavička STKO.");
                }

                var assignedNodes = stasRows
                    .Where(item =>
                        string.Equals(item.BomNumber, mast.BomNumber, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            NormalizeAlternative(item.Alternative),
                            normalizedAlternative,
                            StringComparison.OrdinalIgnoreCase) &&
                        !item.IsDeleted)
                    .ToList();

                if (assignedNodes.Count == 0)
                {
                    var availableAlternatives = stasRows
                        .Where(item => string.Equals(item.BomNumber, mast.BomNumber, StringComparison.OrdinalIgnoreCase))
                        .Select(item => $"{item.Alternative}/{item.NodeNumber}/{item.Counter}/deleted={item.IsDeleted}")
                        .Take(20)
                        .ToList();

                    result.Messages.Add(
                        $"MAST MATNR {mast.MaterialNumber}, STLNR {mast.BomNumber}, STLAL {mast.Alternative}: " +
                        $"nebyly nalezeny žádné aktivní STAS vazby položek. " +
                        $"STAS dostupné pro kusovník: {string.Join(", ", availableAlternatives)}");
                }

                var bomItems = assignedNodes
                    .SelectMany(assignment =>
                        stpoRows.Where(item =>
                            string.Equals(item.BomNumber, assignment.BomNumber, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(item.NodeNumber, assignment.NodeNumber, StringComparison.OrdinalIgnoreCase) &&
                            IsSameCounterOrMissing(item.Counter, assignment.Counter)))
                    .GroupBy(item => BuildBomNodeKey(
                        item.BomNumber,
                        item.NodeNumber,
                        item.Counter))
                    .Select(group => group.First())
                    .OrderBy(item => item.Position)
                    .Select(item => new SapBomItem
                    {
                        Position = item.Position,
                        ComponentNumber = NormalizeMaterialNumber(item.ComponentNumber),
                        Quantity = item.Quantity,
                        Unit = item.Unit,
                        ItemCategory = item.ItemCategory,
                        ItemText = item.ItemText,
                        ScrapPercent = item.ScrapPercent,
                        IsFixedQuantity = item.IsFixedQuantity,
                        NodeNumber = item.NodeNumber,
                        Counter = item.Counter,
                        ComponentKind = ClassifyComponentByNumber(item.ComponentNumber)
                    })
                    .ToList();

                var bom = new SapBom
                {
                    MaterialNumber = NormalizeMaterialNumber(mast.MaterialNumber),
                    Plant = mast.Plant,
                    BomUsage = mast.BomUsage,
                    BomNumber = mast.BomNumber,
                    Alternative = normalizedAlternative,
                    BaseQuantity = stko?.BaseQuantity,
                    BaseUnit = stko?.BaseUnit ?? string.Empty,
                    BomMeaning = GetBomMeaning(mast.Plant),
                    Items = bomItems,
                    ImportedAt = DateTime.Now
                };

                boms.Add(bom);

                result.ImportedBomCount++;
                result.ImportedItemCount += bomItems.Count;
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.Messages.Add(
                    $"Chyba kusovníku MATNR {mast.MaterialNumber}, STLNR {mast.BomNumber}, STLAL {mast.Alternative}: {ex.Message}");
            }
        }

        _repository.SaveAll(boms);

        result.Messages.Insert(
            0,
            $"Uloženo do lokální DMS cache: {result.ImportedBomCount} kusovníků, {result.ImportedItemCount} položek.");

        result.Messages.Add(
            $"STAS vazeb položek: {stasRows.Count}, aktivních: {stasRows.Count(item => !item.IsDeleted)}.");

        return result;
    }

    private static string GetBomMeaning(string plant)
    {
        return plant switch
        {
            "9200" => "Intercompany / mateřský kusovník",
            "2000" => "Lokální dekorační kusovník",
            _ => "Neznámý význam závodu"
        };
    }

    private static string ClassifyComponentByNumber(string materialNumber)
    {
        var value = NormalizeMaterialNumber(materialNumber);

        if (value.StartsWith("10")) return "GlassArticle";
        if (value.StartsWith("11")) return "PurchasedMaterial";
        if (value.StartsWith("13")) return "PackagingMaterial";
        if (value.StartsWith("17")) return "Recipe";
        if (value.StartsWith("21")) return "ExternalPurchasedMaterial";
        if (value.StartsWith("23")) return "ToolFixture";

        return "Unknown";
    }

    private static List<MastRow> LoadMastRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns("MAST", headerMap, SapBomExcelColumnDefinitions.RequiredColumnsForTable("MAST").ToArray());

        var rows = new List<MastRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var materialNumber = GetCell(row, headerMap, "MATNR");

            if (string.IsNullOrWhiteSpace(materialNumber))
            {
                continue;
            }

            rows.Add(new MastRow
            {
                MaterialNumber = materialNumber,
                Plant = GetCell(row, headerMap, "WERKS") ?? string.Empty,
                BomUsage = GetCell(row, headerMap, "STLAN") ?? string.Empty,
                BomNumber = GetCell(row, headerMap, "STLNR") ?? string.Empty,
                Alternative = NormalizeAlternative(GetCell(row, headerMap, "STLAL"))
            });
        }

        return rows;
    }

    private static List<StkoRow> LoadStkoRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns("STKO", headerMap, SapBomExcelColumnDefinitions.RequiredColumnsForTable("STKO").ToArray());

        var rows = new List<StkoRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var bomNumber = GetCell(row, headerMap, "STLNR");

            if (string.IsNullOrWhiteSpace(bomNumber))
            {
                continue;
            }

            rows.Add(new StkoRow
            {
                BomNumber = bomNumber,
                Alternative = GetCell(row, headerMap, "STLAL") ?? string.Empty,
                BomUsage = GetCell(row, headerMap, "STLAN") ?? string.Empty,
                BaseQuantity = ParseDecimal(GetCell(row, headerMap, "BMENG")),
                BaseUnit = GetCell(row, headerMap, "BMEIN") ?? string.Empty
            });
        }

        return rows;
    }

    private static List<StasRow> LoadStasRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns("STAS", headerMap, SapBomExcelColumnDefinitions.RequiredColumnsForTable("STAS").ToArray());

        var rows = new List<StasRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var bomNumber = GetCell(row, headerMap, "STLNR");

            if (string.IsNullOrWhiteSpace(bomNumber))
            {
                continue;
            }

            rows.Add(new StasRow
            {
                BomNumber = bomNumber,
                Alternative = NormalizeAlternative(GetCell(row, headerMap, "STLAL")),
                NodeNumber = NormalizeNode(GetCell(row, headerMap, "STLKN")),
                Counter = NormalizeCounter(GetOptionalCell(row, headerMap, "STASZ")),
                DeletionIndicator = GetOptionalCell(row, headerMap, "LOEKZ") ?? string.Empty
            });
        }

        return rows;
    }

    private static List<StpoRow> LoadStpoRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns("STPO", headerMap, SapBomExcelColumnDefinitions.RequiredColumnsForTable("STPO").ToArray());

        var rows = new List<StpoRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var bomNumber = GetCell(row, headerMap, "STLNR");

            if (string.IsNullOrWhiteSpace(bomNumber))
            {
                continue;
            }

            rows.Add(new StpoRow
            {
                BomNumber = bomNumber,
                Position = GetCell(row, headerMap, "POSNR") ?? string.Empty,
                ComponentNumber = GetCell(row, headerMap, "IDNRK") ?? string.Empty,
                Quantity = ParseDecimal(GetCell(row, headerMap, "MENGE")),
                Unit = GetCell(row, headerMap, "MEINS") ?? string.Empty,
                ItemCategory = GetCell(row, headerMap, "POSTP") ?? string.Empty,
                ItemText = GetOptionalCell(row, headerMap, "POTX1")
                    ?? GetOptionalCell(row, headerMap, "POTX2")
                    ?? string.Empty,
                ScrapPercent = ParseDecimal(GetOptionalCell(row, headerMap, "AUSCH")),
                IsFixedQuantity = ParseSapBool(GetOptionalCell(row, headerMap, "FMENG")),
                NodeNumber = NormalizeNode(GetCell(row, headerMap, "STLKN")),
                Counter = NormalizeCounter(GetOptionalCell(row, headerMap, "STASZ"))
            });
        }

        return rows;
    }

    private static string BuildBomHeaderKey(
        string bomNumber,
        string alternative)
    {
        return $"{bomNumber}|{NormalizeAlternative(alternative)}";
    }

    private static bool IsSameCounterOrMissing(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBomNodeKey(
        string bomNumber,
        string nodeNumber,
        string counter)
    {
        return $"{bomNumber}|{nodeNumber}|{counter}";
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet worksheet)
    {
        var firstRow = worksheet.FirstRowUsed();

        if (firstRow is null)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in firstRow.CellsUsed())
        {
            var header = cell.GetString().Trim();

            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var normalizedHeader = header.ToUpperInvariant();

            // SE16N export s popisnými názvy polí může obsahovat duplicitní hlavičky,
            // například "ITEM NODE". Pro import bereme první výskyt
            // a další duplicitní popisné sloupce ignorujeme.
            if (headerMap.ContainsKey(normalizedHeader))
            {
                continue;
            }

            headerMap.Add(normalizedHeader, cell.Address.ColumnNumber);
        }

        return headerMap;
    }

    private static void RequireColumns(
        string tableName,
        Dictionary<string, int> headerMap,
        params string[] requiredColumns)
    {
        var missingColumns = requiredColumns
            .Where(column => !headerMap.ContainsKey(column))
            .ToList();

        if (missingColumns.Count == 0)
        {
            return;
        }

        var availableColumns = headerMap.Keys
            .OrderBy(item => item)
            .ToList();

        throw new InvalidOperationException(
            $"V exportu {tableName} chybí povinný sloupec/sloupce: {string.Join(", ", missingColumns)}\n\n" +
            $"Zkontroluj, že jsou hlavičky v Excelu přejmenované na technické SAP názvy.\n\n" +
            $"Dostupné sloupce v exportu {tableName}:\n" +
            string.Join(", ", availableColumns));
    }

    private static string? GetCell(
        IXLRow row,
        Dictionary<string, int> headerMap,
        string columnName)
    {
        if (!headerMap.TryGetValue(columnName, out var columnNumber))
        {
            return null;
        }

        var cell = row.Cell(columnNumber);

        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble()
                .ToString(CultureInfo.InvariantCulture);
        }

        return cell.GetString().Trim();
    }

    private static string? GetOptionalCell(
        IXLRow row,
        Dictionary<string, int> headerMap,
        string columnName)
    {
        return GetCell(row, headerMap, columnName);
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value
            .Trim()
            .Replace("\u00A0", "")
            .Replace(" ", "");

        if (decimal.TryParse(
                text,
                NumberStyles.Any,
                new CultureInfo("cs-CZ"),
                out var czValue))
        {
            return czValue;
        }

        text = text.Replace(',', '.');

        if (decimal.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var invariantValue))
        {
            return invariantValue;
        }

        return null;
    }

    private static string NormalizeMaterialNumber(string value)
    {
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.All(char.IsDigit)
            ? value.PadLeft(10, '0')
            : value;
    }

    private static bool ParseSapBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();

        return normalized is "X" or "1" or "TRUE" or "YES" or "ANO";
    }

    private static string NormalizeAlternative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString("00")
            : text;
    }

    private static string NormalizeNode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString()
            : text;
    }

    private static string NormalizeCounter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        return int.TryParse(text, out var number)
            ? number.ToString()
            : text;
    }

    private sealed class MastRow
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
        public string BomUsage { get; init; } = string.Empty;
        public string BomNumber { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
    }

    private sealed class StkoRow
    {
        public string BomNumber { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
        public string BomUsage { get; init; } = string.Empty;
        public decimal? BaseQuantity { get; init; }
        public string BaseUnit { get; init; } = string.Empty;
    }

    private sealed class StasRow
    {
        public string BomNumber { get; init; } = string.Empty;       // STLNR
        public string Alternative { get; init; } = string.Empty;     // STLAL
        public string NodeNumber { get; init; } = string.Empty;      // STLKN
        public string Counter { get; init; } = string.Empty;         // STASZ
        public string DeletionIndicator { get; init; } = string.Empty; // LOEKZ

        public bool IsDeleted =>
            !string.IsNullOrWhiteSpace(DeletionIndicator);
    }

    private sealed class StpoRow
    {
        public string BomNumber { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string ComponentNumber { get; init; } = string.Empty;
        public decimal? Quantity { get; init; }
        public string Unit { get; init; } = string.Empty;
        public string ItemCategory { get; init; } = string.Empty;
        public string ItemText { get; init; } = string.Empty;
        public decimal? ScrapPercent { get; init; }
        public bool IsFixedQuantity { get; init; }
        public string NodeNumber { get; init; } = string.Empty;
        public string Counter { get; init; } = string.Empty;
    }
}