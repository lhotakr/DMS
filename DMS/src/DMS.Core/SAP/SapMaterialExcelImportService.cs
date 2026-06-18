using ClosedXML.Excel;

namespace DMS.Core.Sap;

public sealed class SapMaterialExcelImportService
{
    private readonly SapMaterialClassifier _classifier;
    private readonly JsonSapMaterialRepository _repository;

    public SapMaterialExcelImportService(
        SapMaterialClassifier classifier,
        JsonSapMaterialRepository repository)
    {
        _classifier = classifier;
        _repository = repository;
    }

    public SapMaterialImportResult Import(string maraFilePath, string maktFilePath)
    {
        var result = new SapMaterialImportResult();

        var maraRows = LoadMaraRows(maraFilePath);
        var maktRows = LoadMaktRows(maktFilePath);

        result.MaraRows = maraRows.Count;
        result.MaktRows = maktRows.Count;

        var maktByMaterialNumber = maktRows
            .GroupBy(item => item.MaterialNumber)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var importedMaterials = new List<SapMaterial>();

        foreach (var mara in maraRows)
        {
            try
            {
                if (!maktByMaterialNumber.TryGetValue(mara.MaterialNumber, out var makt))
                {
                    result.ErrorRows++;
                    result.Messages.Add($"MAKT nenalezen pro materiál {mara.MaterialNumber}");
                    continue;
                }

                result.JoinedRows++;

                var description = makt.Description ?? string.Empty;

                var rule = _classifier.Classify(
                    mara.MaterialNumber,
                    description,
                    mara.OldMaterialNumber ?? string.Empty);

                if (rule is null || !rule.Import)
                {
                    result.IgnoredRows++;
                    continue;
                }

                var glassInfo = string.Equals(
                    rule.MaterialKind,
                    nameof(SapMaterialKind.GlassArticle),
                    StringComparison.OrdinalIgnoreCase)
                    ? SapArticleTextParser.TryParseGlassArticleText(description)
                    : null;

                var packagingInfo = string.Equals(
                    rule.MaterialKind,
                    nameof(SapMaterialKind.Packaging),
                    StringComparison.OrdinalIgnoreCase)
                    ? SapPackagingTextParser.Parse(description)
                    : null;

                var toolFixtureKind = GetToolFixtureKind(
                    rule.MaterialKind,
                    description);

                importedMaterials.Add(new SapMaterial
                {
                    MaterialNumber = mara.MaterialNumber,
                    Description = description,
                    OldMaterialNumber = mara.OldMaterialNumber,
                    MaterialStatus = mara.MaterialStatus,
                    MaterialKind = rule.MaterialKind,

                    // Dočasně plníme staré pole ze sap-material-rules.
                    // Později bych ho v SapMaterial přejmenoval spíš na SapNumberPrefix.
                    TransactionPrefix = rule.SapNumberPrefix,

                    ToolFixtureKind = toolFixtureKind,
                    GlassInfo = glassInfo,
                    PackagingInfo = packagingInfo,
                    ImportedAt = DateTime.Now
                });

                result.ImportedRows++;
            }
            catch (Exception ex)
            {
                result.ErrorRows++;
                result.Messages.Add($"Chyba u MATNR {mara.MaterialNumber}: {ex.Message}");
            }
        }

        _repository.SaveAll(importedMaterials);

        result.Messages.Insert(
            0,
            $"Uloženo do lokální DMS cache: {result.ImportedRows} materiálů.");

        return result;
    }

    private static string? GetToolFixtureKind(string materialKind, string description)
    {
        if (!string.Equals(
                materialKind,
                nameof(SapMaterialKind.ToolFixture),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return description.TrimStart().StartsWith("WKZ", StringComparison.OrdinalIgnoreCase)
            ? "MachineTool"
            : "SprayMetalizationFixture";
    }

    private static List<MaraRow> LoadMaraRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();

        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(headerMap, "MATNR", "BISMT", "MSTAE");

        var rows = new List<MaraRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var materialNumber = GetCell(row, headerMap, "MATNR");

            if (string.IsNullOrWhiteSpace(materialNumber))
            {
                continue;
            }

            rows.Add(new MaraRow
            {
                MaterialNumber = NormalizeMaterialNumber(materialNumber),
                OldMaterialNumber = GetCell(row, headerMap, "BISMT"),
                MaterialStatus = GetCell(row, headerMap, "MSTAE")
            });
        }

        return rows;
    }

    private static List<MaktRow> LoadMaktRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();

        var headerMap = BuildHeaderMap(worksheet);

        RequireColumns(headerMap, "MATNR", "MAKTX");

        var rows = new List<MaktRow>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var materialNumber = GetCell(row, headerMap, "MATNR");

            if (string.IsNullOrWhiteSpace(materialNumber))
            {
                continue;
            }

            rows.Add(new MaktRow
            {
                MaterialNumber = NormalizeMaterialNumber(materialNumber),
                Description = GetCell(row, headerMap, "MAKTX") ?? string.Empty
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

        return firstRow.CellsUsed()
            .ToDictionary(
                cell => cell.GetString().Trim().ToUpperInvariant(),
                cell => cell.Address.ColumnNumber,
                StringComparer.OrdinalIgnoreCase);
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

    private static string NormalizeMaterialNumber(string value)
    {
        return value.Trim().PadLeft(10, '0');
    }

    private sealed class MaraRow
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string? OldMaterialNumber { get; init; }
        public string? MaterialStatus { get; init; }
    }

    private sealed class MaktRow
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}