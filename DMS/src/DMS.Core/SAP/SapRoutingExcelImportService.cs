using ClosedXML.Excel;
using System.Globalization;

namespace DMS.Core.Sap;

public sealed class SapRoutingExcelImportService
{
    private readonly JsonSapRoutingRepository _repository;

    public SapRoutingExcelImportService(JsonSapRoutingRepository repository)
    {
        _repository = repository;
    }

    public SapRoutingImportResult Import(
    string maplFilePath,
    string plkoFilePath,
    string plasFilePath,
    string plpoFilePath)
    {
        var result = new SapRoutingImportResult();

        var maplRows = LoadMaplRows(maplFilePath);
        var plkoRows = LoadPlkoRows(plkoFilePath);
        var plasRows = LoadPlasRows(plasFilePath);
        var plpoRows = LoadPlpoRows(plpoFilePath);

        result.MaplRows = maplRows.Count;
        result.PlkoRows = plkoRows.Count;
        result.PlpoRows = plpoRows.Count;

        // Teď už importujeme všechny alternativy.
        // Dříve se brala jen PLNAL = 1 / 01, ale TEC03 už varianty umí zobrazit zvlášť.
        result.SkippedAlternativeCount = 0;

        var plkoByKey = plkoRows
            .GroupBy(item => BuildRoutingKey(
                item.TaskListType,
                item.GroupNumber,
                NormalizeAlternative(item.Alternative)))
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var routings = new List<SapRouting>();

        foreach (var mapl in maplRows)
        {
            try
            {
                var normalizedAlternative = NormalizeAlternative(mapl.Alternative);

                var headerKey = BuildRoutingKey(
                    mapl.TaskListType,
                    mapl.GroupNumber,
                    normalizedAlternative);

                plkoByKey.TryGetValue(headerKey, out var plko);

                if (plko is null)
                {
                    result.Messages.Add(
                        $"MAPL MATNR {mapl.MaterialNumber}, PLNNR {mapl.GroupNumber}, PLNAL {mapl.Alternative}: nebyla nalezena hlavička PLKO.");
                }

                var assignedNodes = plasRows
                    .Where(item =>
                        string.Equals(item.TaskListType, mapl.TaskListType, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.GroupNumber, mapl.GroupNumber, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            NormalizeAlternative(item.Alternative),
                            normalizedAlternative,
                            StringComparison.OrdinalIgnoreCase) &&
                        !item.IsDeleted)
                    .ToList();

                if (assignedNodes.Count == 0)
                {
                    result.Messages.Add(
                        $"MAPL MATNR {mapl.MaterialNumber}, PLNNR {mapl.GroupNumber}, PLNAL {mapl.Alternative}: nebyly nalezeny žádné aktivní PLAS vazby operací.");
                }

                var routingOperations = assignedNodes
                    .SelectMany(assignment => FindPlpoRowsForAssignment(plpoRows, assignment))
                    .GroupBy(operation => BuildOperationNodeKey(
                        operation.TaskListType,
                        operation.GroupNumber,
                        operation.NodeNumber,
                        operation.Counter))
                    .Select(group => group.First())
                    .OrderBy(operation => operation.OperationNumber)
                    .Select(item => new SapRoutingOperation
                    {
                        OperationNumber = item.OperationNumber,
                        WorkCenterObjectId = item.WorkCenterObjectId,
                        ControlKey = item.ControlKey,
                        Description = item.Description,

                        BaseQuantity = item.BaseQuantity,
                        BaseUnit = item.BaseUnit,

                        Vgw01 = item.Vgw01,
                        Vge01 = item.Vge01,

                        Vgw03 = item.Vgw03,
                        Vge03 = item.Vge03,

                        Vgw04 = item.Vgw04,
                        Vge04 = item.Vge04,

                        InfoRecord = item.InfoRecord,
                        OperationMeaning = GetOperationMeaning(mapl.Plant, item),
                        ScrapPercent = item.ScrapPercent,

                        NodeNumber = item.NodeNumber,
                        Counter = item.Counter
                    })
                    .ToList();

                if (assignedNodes.Count > 0 && routingOperations.Count == 0)
                {
                    var assignedNodeText = string.Join(
                        ", ",
                        assignedNodes
                            .Take(10)
                            .Select(item => $"{item.NodeNumber}/{item.Counter}"));

                    var availablePlpoNodeText = string.Join(
                        ", ",
                        plpoRows
                            .Where(item =>
                                string.Equals(item.TaskListType, mapl.TaskListType, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(item.GroupNumber, mapl.GroupNumber, StringComparison.OrdinalIgnoreCase))
                            .Take(10)
                            .Select(item => $"{item.NodeNumber}/{item.Counter}/{item.OperationNumber}"));

                    result.Messages.Add(
                        $"MAPL MATNR {mapl.MaterialNumber}, PLNNR {mapl.GroupNumber}, PLNAL {mapl.Alternative}: " +
                        $"PLAS vazby existují, ale nebyly nalezeny odpovídající PLPO operace. " +
                        $"PLAS uzly: {assignedNodeText}. PLPO uzly ve skupině: {availablePlpoNodeText}.");
                }

                var routing = new SapRouting
                {
                    MaterialNumber = NormalizeMaterialNumber(mapl.MaterialNumber),
                    Plant = mapl.Plant,
                    TaskListType = mapl.TaskListType,
                    GroupNumber = mapl.GroupNumber,
                    Alternative = normalizedAlternative,

                    Description = plko?.Description ?? string.Empty,
                    Status = plko?.Status ?? string.Empty,
                    Usage = plko?.Usage ?? string.Empty,

                    RoutingMeaning = GetRoutingMeaning(mapl.Plant),
                    Operations = routingOperations,
                    ImportedAt = DateTime.Now
                };

                ValidateRouting(routing, result);

                routings.Add(routing);

                result.ImportedRoutingCount++;
                result.ImportedOperationCount += routingOperations.Count;
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.Messages.Add(
                    $"Chyba postupu MATNR {mapl.MaterialNumber}, PLNNR {mapl.GroupNumber}, PLNAL {mapl.Alternative}: {ex.Message}");
            }
        }

        _repository.SaveAll(routings);

        result.Messages.Insert(
            0,
            $"Uloženo do lokální DMS cache: {result.ImportedRoutingCount} pracovních postupů, {result.ImportedOperationCount} operací.");

        result.Messages.Add(
            $"PLAS vazeb operací: {plasRows.Count}, aktivních: {plasRows.Count(item => !item.IsDeleted)}.");

        return result;
    }

    private static void ValidateRouting(SapRouting routing, SapRoutingImportResult result)
    {
        if (routing.Operations.Count == 0)
        {
            routing.HasCriticalError = true;
            routing.ValidationMessages.Add("Pracovní postup nemá žádné operace.");
            result.WarningCount++;
            return;
        }

        if (routing.Plant == "9200")
        {
            ValidatePlant9200(routing, result);
            return;
        }

        if (routing.Plant == "2000")
        {
            ValidatePlant2000(routing, result);
        }
    }

    private static void ValidatePlant9200(SapRouting routing, SapRoutingImportResult result)
    {
        var firstOperation = routing.Operations
            .OrderBy(item => item.OperationNumber)
            .First();

        if (!string.Equals(firstOperation.ControlKey, "ZPP5", StringComparison.OrdinalIgnoreCase))
        {
            routing.HasCriticalError = true;
            routing.ValidationMessages.Add(
                $"9200: první operace {firstOperation.OperationNumber} nemá řídicí klíč ZPP5.");
            result.WarningCount++;
        }

        if (string.IsNullOrWhiteSpace(firstOperation.InfoRecord))
        {
            routing.HasCriticalError = true;
            routing.ValidationMessages.Add(
                $"9200: první operace {firstOperation.OperationNumber} nemá vyplněné INFNR. Nelze vyrábět.");
            result.WarningCount++;
        }

        foreach (var operation in routing.Operations.Skip(1))
        {
            if (!string.Equals(operation.ControlKey, "ZPP1", StringComparison.OrdinalIgnoreCase))
            {
                routing.ValidationMessages.Add(
                    $"9200: operace {operation.OperationNumber} nemá řídicí klíč ZPP1.");
                result.WarningCount++;
            }
        }
    }

    private static void ValidatePlant2000(SapRouting routing, SapRoutingImportResult result)
    {
        var orderedOperations = routing.Operations
            .OrderBy(item => item.OperationNumber)
            .ToList();

        var lastOperation = orderedOperations.Last();

        foreach (var operation in orderedOperations.Take(orderedOperations.Count - 1))
        {
            if (!string.Equals(operation.ControlKey, "ZPP1", StringComparison.OrdinalIgnoreCase))
            {
                routing.ValidationMessages.Add(
                    $"2000: operace {operation.OperationNumber} nemá řídicí klíč ZPP1.");
                result.WarningCount++;
            }
        }

        if (!string.Equals(lastOperation.ControlKey, "ZPP2", StringComparison.OrdinalIgnoreCase))
        {
            routing.ValidationMessages.Add(
                $"2000: poslední operace {lastOperation.OperationNumber} nemá řídicí klíč ZPP2.");
            result.WarningCount++;
        }

        foreach (var operation in orderedOperations)
        {
            if (operation.BaseQuantity is null)
            {
                routing.ValidationMessages.Add(
                    $"2000: operace {operation.OperationNumber} nemá BMSCH.");
                result.WarningCount++;
            }

            if (operation.Vgw03 is null)
            {
                routing.ValidationMessages.Add(
                    $"2000: operace {operation.OperationNumber} nemá VGW03.");
                result.WarningCount++;
            }

            if (operation.Vgw04 is null)
            {
                routing.ValidationMessages.Add(
                    $"2000: operace {operation.OperationNumber} nemá VGW04 / počet lidí.");
                result.WarningCount++;
            }
        }
    }

    private static string GetRoutingMeaning(string plant)
    {
        return plant switch
        {
            "9200" => "Intercompany / skupinový pracovní postup",
            "2000" => "Lokální konkrétní pracovní postup",
            _ => "Neznámý význam závodu"
        };
    }

    private static string GetOperationMeaning(string plant, PlpoRow operation)
    {
        if (plant == "9200")
        {
            if (operation.ControlKey.Equals("ZPP5", StringComparison.OrdinalIgnoreCase))
            {
                return "První intercompany operace s INFNR";
            }

            if (operation.ControlKey.Equals("ZPP1", StringComparison.OrdinalIgnoreCase))
            {
                return "Následující intercompany operace";
            }
        }

        if (plant == "2000")
        {
            if (operation.ControlKey.Equals("ZPP2", StringComparison.OrdinalIgnoreCase))
            {
                return "Poslední lokální operace";
            }

            if (operation.ControlKey.Equals("ZPP1", StringComparison.OrdinalIgnoreCase))
            {
                return "Lokální výrobní operace";
            }
        }

        return string.Empty;
    }

    private static List<MaplRow> LoadMaplRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(
            headerMap,
            SapRoutingExcelColumnDefinitions.RequiredColumnsForTable("MAPL").ToArray());

        var rows = new List<MaplRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var materialNumber = GetCell(row, headerMap, "MATNR");

            if (string.IsNullOrWhiteSpace(materialNumber))
            {
                continue;
            }

            rows.Add(new MaplRow
            {
                MaterialNumber = materialNumber,
                Plant = GetCell(row, headerMap, "WERKS") ?? string.Empty,
                TaskListType = GetCell(row, headerMap, "PLNTY") ?? string.Empty,
                GroupNumber = GetCell(row, headerMap, "PLNNR") ?? string.Empty,
                Alternative = NormalizeAlternative(GetCell(row, headerMap, "PLNAL") ?? string.Empty)
            });
        }

        return rows;
    }

    private static List<PlkoRow> LoadPlkoRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(
            headerMap,
            SapRoutingExcelColumnDefinitions.RequiredColumnsForTable("PLKO").ToArray());

        var rows = new List<PlkoRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var groupNumber = GetCell(row, headerMap, "PLNNR");

            if (string.IsNullOrWhiteSpace(groupNumber))
            {
                continue;
            }

            rows.Add(new PlkoRow
            {
                TaskListType = GetCell(row, headerMap, "PLNTY") ?? string.Empty,
                GroupNumber = groupNumber,
                Alternative = NormalizeAlternative(GetCell(row, headerMap, "PLNAL") ?? string.Empty),
                Description = GetOptionalCell(row, headerMap, "KTEXT") ?? string.Empty,
                Status = GetOptionalCell(row, headerMap, "STATU") ?? string.Empty,
                Usage = GetOptionalCell(row, headerMap, "VERWE") ?? string.Empty,

            });
        }

        return rows;
    }

    private static List<PlasRow> LoadPlasRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(
            headerMap,
            SapRoutingExcelColumnDefinitions.RequiredColumnsForTable("PLAS").ToArray());

        var rows = new List<PlasRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var groupNumber = GetCell(row, headerMap, "PLNNR");

            if (string.IsNullOrWhiteSpace(groupNumber))
            {
                continue;
            }

            rows.Add(new PlasRow
            {
                TaskListType = GetCell(row, headerMap, "PLNTY") ?? string.Empty,
                GroupNumber = groupNumber,
                Alternative = NormalizeAlternative(GetCell(row, headerMap, "PLNAL")),
                NodeNumber = NormalizeNode(GetCell(row, headerMap, "PLNKN")),
                Counter = NormalizeCounter(GetOptionalCell(row, headerMap, "ZAEHL")),
                DeletionIndicator = GetOptionalCell(row, headerMap, "LOEKZ") ?? string.Empty
            });
        }

        return rows;
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

    private static List<PlpoRow> LoadPlpoRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(
            headerMap,
            SapRoutingExcelColumnDefinitions.RequiredColumnsForTable("PLPO").ToArray());

        var rows = new List<PlpoRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var groupNumber = GetCell(row, headerMap, "PLNNR");

            if (string.IsNullOrWhiteSpace(groupNumber))
            {
                continue;
            }

            rows.Add(new PlpoRow
            {
                TaskListType = GetCell(row, headerMap, "PLNTY") ?? string.Empty,
                GroupNumber = groupNumber,
                OperationNumber = GetCell(row, headerMap, "VORNR") ?? string.Empty,
                WorkCenterObjectId = GetCell(row, headerMap, "ARBID") ?? string.Empty,
                ControlKey = GetCell(row, headerMap, "STEUS") ?? string.Empty,
                Description = GetOptionalCell(row, headerMap, "LTXA1") ?? string.Empty,
                BaseQuantity = ParseDecimal(GetCell(row, headerMap, "BMSCH")),
                BaseUnit = GetOptionalCell(row, headerMap, "MEINH") ?? string.Empty,
                Vgw01 = ParseDecimal(GetCell(row, headerMap, "VGW01")),
                Vge01 = GetOptionalCell(row, headerMap, "VGE01") ?? string.Empty,
                Vgw03 = ParseDecimal(GetCell(row, headerMap, "VGW03")),
                Vge03 = GetOptionalCell(row, headerMap, "VGE03") ?? string.Empty,
                Vgw04 = ParseDecimal(GetCell(row, headerMap, "VGW04")),
                Vge04 = GetOptionalCell(row, headerMap, "VGE04") ?? string.Empty,
                InfoRecord = GetOptionalCell(row, headerMap, "INFNR") ?? string.Empty,
                ScrapPercent = ParseDecimal(GetOptionalCell(row, headerMap, "AUSSS")),
                NodeNumber = NormalizeNode(GetCell(row, headerMap, "PLNKN")),
                Counter = NormalizeCounter(GetOptionalCell(row, headerMap, "ZAEHL"))
            });
        }

        return rows;
    }

    private static string BuildRoutingKey(
        string taskListType,
        string groupNumber,
        string alternative)
    {
        return $"{taskListType}|{groupNumber}|{NormalizeAlternative(alternative)}";
    }

    private static string BuildOperationGroupKey(
        string taskListType,
        string groupNumber)
    {
        return $"{taskListType}|{groupNumber}";
    }

    private static bool IsStandardAlternative(string alternative)
    {
        var normalized = NormalizeAlternative(alternative);

        return normalized == "1";
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

            // SE16N export s popisnými názvy může obsahovat duplicitní hlavičky,
            // například "DELETION INDICATOR". Pro import bereme první výskyt
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
        Dictionary<string, int> headerMap,
        params string[] requiredColumns)
    {
        foreach (var column in requiredColumns)
        {
            if (!headerMap.ContainsKey(column))
            {
                throw new InvalidOperationException(
                    $"V Excelu chybí povinný sloupec: {column}");
            }
        }
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

        return row.Cell(columnNumber).GetString().Trim();
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

        value = value.Trim().Replace(',', '.');

        if (decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return number;
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

    private static bool IsSameCounterOrMissing(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOperationNodeKey(
        string taskListType,
        string groupNumber,
        string nodeNumber,
        string counter)
    {
        return $"{taskListType}|{groupNumber}|{nodeNumber}|{counter}";
    }

    private static IEnumerable<PlpoRow> FindPlpoRowsForAssignment(
    IReadOnlyList<PlpoRow> plpoRows,
    PlasRow assignment)
    {
        var sameNodeRows = plpoRows
            .Where(operation =>
                string.Equals(operation.TaskListType, assignment.TaskListType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(operation.GroupNumber, assignment.GroupNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(operation.NodeNumber, assignment.NodeNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameNodeRows.Count == 0)
        {
            return Array.Empty<PlpoRow>();
        }

        // Nejprve přesný pokus včetně counteru.
        if (!string.IsNullOrWhiteSpace(assignment.Counter))
        {
            var sameCounterRows = sameNodeRows
                .Where(operation =>
                    string.IsNullOrWhiteSpace(operation.Counter) ||
                    string.Equals(operation.Counter, assignment.Counter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sameCounterRows.Count > 0)
            {
                return sameCounterRows;
            }
        }

        // Fallback: SAP exporty někdy mají ZAEHL jinak formátovaný nebo je counter z jiné úrovně.
        // PLNKN je pro nás hlavní vazba PLAS -> PLPO.
        return sameNodeRows;
    }

    private sealed class MaplRow
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
        public string TaskListType { get; init; } = string.Empty;
        public string GroupNumber { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
    }

    private sealed class PlkoRow
    {
        public string TaskListType { get; init; } = string.Empty;
        public string GroupNumber { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Usage { get; init; } = string.Empty;
    }

    private sealed class PlasRow
    {
        public string TaskListType { get; init; } = string.Empty; // PLNTY
        public string GroupNumber { get; init; } = string.Empty;  // PLNNR
        public string Alternative { get; init; } = string.Empty;  // PLNAL
        public string NodeNumber { get; init; } = string.Empty;   // PLNKN
        public string Counter { get; init; } = string.Empty;      // ZAEHL
        public string DeletionIndicator { get; init; } = string.Empty; // LOEKZ

        public bool IsDeleted =>
            !string.IsNullOrWhiteSpace(DeletionIndicator);
    }
    private sealed class PlpoRow
    {
        public string TaskListType { get; init; } = string.Empty;
        public string GroupNumber { get; init; } = string.Empty;
        public string OperationNumber { get; init; } = string.Empty;
        public string WorkCenterObjectId { get; init; } = string.Empty;
        public string ControlKey { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal? BaseQuantity { get; init; }
        public string BaseUnit { get; init; } = string.Empty;
        public decimal? Vgw01 { get; init; }
        public string Vge01 { get; init; } = string.Empty;
        public decimal? Vgw03 { get; init; }
        public string Vge03 { get; init; } = string.Empty;
        public decimal? Vgw04 { get; init; }
        public string Vge04 { get; init; } = string.Empty;
        public decimal? ScrapPercent { get; init; }
        public string InfoRecord { get; init; } = string.Empty;
        public string NodeNumber { get; init; } = string.Empty;
        public string Counter { get; init; } = string.Empty;
    }
}