using ClosedXML.Excel;

namespace DMS.Core.Sap;

public sealed class SapWorkCenterExcelImportService
{
    private readonly JsonSapWorkCenterRepository _repository;

    public SapWorkCenterExcelImportService(JsonSapWorkCenterRepository repository)
    {
        _repository = repository;
    }

    public SapWorkCenterImportResult Import(
        string crhdFilePath,
        string crtxFilePath)
    {
        var result = new SapWorkCenterImportResult();

        var crhdRows = LoadCrhdRows(crhdFilePath);
        var crtxRows = LoadCrtxRows(crtxFilePath);

        result.CrhdRows = crhdRows.Count;
        result.CrtxRows = crtxRows.Count;

        var textsByObjectId = crtxRows
            .GroupBy(item => item.ObjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(text => new SapWorkCenterText
                    {
                        Language = text.Language,
                        Text = text.Text
                    })
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var workCenters = new List<SapWorkCenter>();

        foreach (var crhd in crhdRows)
        {
            try
            {
                textsByObjectId.TryGetValue(crhd.ObjectId, out var texts);

                var workCenter = new SapWorkCenter
                {
                    ObjectId = crhd.ObjectId,
                    WorkCenter = crhd.WorkCenter,
                    Plant = crhd.Plant,
                    Texts = texts ?? new List<SapWorkCenterText>(),
                    ImportedAt = DateTime.Now
                };

                workCenters.Add(workCenter);

                result.ImportedWorkCenterCount++;
                result.ImportedTextCount += workCenter.Texts.Count;
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.Messages.Add(
                    $"Chyba pracoviště OBJID {crhd.ObjectId}: {ex.Message}");
            }
        }

        _repository.SaveAll(workCenters);

        result.Messages.Insert(
            0,
            $"Uloženo do lokální DMS cache: {result.ImportedWorkCenterCount} pracovišť, {result.ImportedTextCount} textů.");

        return result;
    }

    private static List<CrhdRow> LoadCrhdRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(
            headerMap,
            SapWorkCenterExcelColumnDefinitions.RequiredColumnsForTable("CRHD").ToArray());

        var rows = new List<CrhdRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var objectId = GetCell(row, headerMap, "OBJID");

            if (string.IsNullOrWhiteSpace(objectId))
            {
                continue;
            }

            rows.Add(new CrhdRow
            {
                ObjectId = objectId,
                WorkCenter = GetCell(row, headerMap, "ARBPL") ?? string.Empty,
                Plant = GetOptionalCell(row, headerMap, "WERKS") ?? string.Empty
            });
        }

        return rows;
    }

    private static List<CrtxRow> LoadCrtxRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(
            headerMap,
            SapWorkCenterExcelColumnDefinitions.RequiredColumnsForTable("CRTX").ToArray());

        var rows = new List<CrtxRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var objectId = GetCell(row, headerMap, "OBJID");

            if (string.IsNullOrWhiteSpace(objectId))
            {
                continue;
            }

            rows.Add(new CrtxRow
            {
                ObjectId = objectId,
                Language = GetOptionalCell(row, headerMap, "SPRAS") ?? string.Empty,
                Text = GetOptionalCell(row, headerMap, "KTEXT") ?? string.Empty
            });
        }

        return rows;
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

    private sealed class CrhdRow
    {
        public string ObjectId { get; init; } = string.Empty;
        public string WorkCenter { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
    }

    private sealed class CrtxRow
    {
        public string ObjectId { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
    }
}