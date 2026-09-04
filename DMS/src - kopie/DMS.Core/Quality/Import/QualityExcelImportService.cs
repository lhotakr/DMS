using ClosedXML.Excel;
using DMS.Core.Sap;

namespace DMS.Core.Quality.Import;

public sealed class QualityExcelImportService
{
    private readonly JsonQualityRepository _repository;
    private readonly Dictionary<string, SapMaterial> _sapMaterialsByNumber;
    private readonly SapDecorationRuleService? _decorationRuleService;

    public QualityExcelImportService(
        JsonQualityRepository repository,
        IReadOnlyList<SapMaterial>? sapMaterials = null,
        SapDecorationRuleService? decorationRuleService = null)
    {
        _repository = repository;
        _decorationRuleService = decorationRuleService;

        _sapMaterialsByNumber = (sapMaterials ?? Array.Empty<SapMaterial>())
            .Where(item => !string.IsNullOrWhiteSpace(item.MaterialNumber))
            .GroupBy(item => NormalizeSapNumber(item.MaterialNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }
    private string GetDecorationCodeFromSapCache(string sapMaterialNumber)
    {
        var normalizedSapNumber = NormalizeSapNumber(sapMaterialNumber);

        if (string.IsNullOrWhiteSpace(normalizedSapNumber))
        {
            return string.Empty;
        }

        if (!_sapMaterialsByNumber.TryGetValue(normalizedSapNumber, out var material))
        {
            return string.Empty;
        }

        if (material.GlassInfo is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(material.GlassInfo.DecorationChain))
        {
            return material.GlassInfo.DecorationChain;
        }

        if (material.GlassInfo.DecorationSteps.Count > 0)
        {
            return string.Join("", material.GlassInfo.DecorationSteps);
        }

        return string.Empty;
    }

    public QualityExcelImportResult ImportArticles(string filePath)
    {
        try
        {
            var warnings = new List<string>();
            var rows = ReadRows(filePath);

            var articles = new List<QualityArticle>();

            foreach (var row in rows)
            {
                var legacyArticleNumber = NormalizeText(Get(row, "Artikl"));

                if (string.IsNullOrWhiteSpace(legacyArticleNumber))
                {
                    continue;
                }

                articles.Add(new QualityArticle
                {
                    LegacyArticleNumber = legacyArticleNumber,
                    Title = NormalizeSharePointValue(Get(row, "Title")),
                    Prefix = NormalizeSharePointValue(Get(row, "Předčíslí artiklu")),
                    ArticleNumberPart = NormalizeSharePointValue(Get(row, "Číslo artiklu")),
                    ImportantInfo = NormalizeSharePointValue(Get(row, "Důležité info")),
                    Notes = NormalizeSharePointValue(Get(row, "Poznámky")),
                    ImportedAt = DateTime.Now,
                    SourceFilePath = filePath,
                    
                    Metadata = new QualityRecordMetadata
                    {
                        CreatedBy = "IMPORT",
                        CreatedAt = DateTime.Now,
                        ModifiedBy = string.Empty,
                        ModifiedAt = null
                    }
                });
            }

            articles = articles
                .GroupBy(item => item.LegacyArticleNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.LegacyArticleNumber)
                .ToList();

            _repository.SaveArticles(articles);

            return QualityExcelImportResult.Ok(
                $"Import artiklů dokončen. Importováno: {articles.Count}",
                articles.Count,
                warnings);
        }
        catch (Exception ex)
        {
            return QualityExcelImportResult.Fail(
                $"Import artiklů se nepodařil.\n\n{ex.Message}");
        }
    }

    public QualityExcelImportResult ImportPrintVersions(string filePath)
    {
        try
        {
            var warnings = new List<string>();
            var rows = ReadRows(filePath);

            var printVersions = new List<QualityPrintVersion>();

            foreach (var row in rows)
            {
                var fullPrintVersionNumber = NormalizeText(Get(row, "Celý název tiskové verze"));

                if (string.IsNullOrWhiteSpace(fullPrintVersionNumber))
                {
                    continue;
                }

                var sapMaterialNumber = NormalizeSapNumber(Get(row, "SAP"));

                var tasks = new List<QualityTask>();

                for (var i = 1; i <= 8; i++)
                {
                    var text = NormalizeText(Get(row, $"Úkol_{i}"));
                    var completedAt = GetDate(row, $"ÚkolDatum_{i}");

                    if (string.IsNullOrWhiteSpace(text) && completedAt is null)
                    {
                        continue;
                    }

                    tasks.Add(new QualityTask
                    {
                        Number = i,
                        Text = text,
                        CompletedAt = completedAt
                    });
                }

                printVersions.Add(new QualityPrintVersion
                {
                    FullPrintVersionNumber = fullPrintVersionNumber,
                    LegacyArticleNumber = NormalizeText(Get(row, "Artikl")),
                    GlassType = NormalizeText(Get(row, "Typ skla")),
                    VersionNumber = NormalizeText(Get(row, "Číslo tiskové verze")),
                    SapMaterialNumber = sapMaterialNumber,
                    Title = NormalizeText(Get(row, "Title")),
                    Customer = FirstNotEmpty(
                        NormalizeSharePointValue(Get(row, "Zákazník")),
                        NormalizeSharePointValue(Get(row, "Zákazník2"))),
                    ColorType = NormalizeSharePointValue(Get(row, "Barva")),
                    GlassTreatment = NormalizeSharePointValue(Get(row, "ÚpravaSkla")),

                    DecorationCode = FirstNotEmpty(
                        NormalizeSharePointValue(Get(row, "Dekorace")),
                        NormalizeSharePointValue(Get(row, "Typ dekorace")),
                        NormalizeSharePointValue(Get(row, "Decoration")),
                        NormalizeSharePointValue(Get(row, "DecorationName")),
                        GetQualityDecorationNameFromSapCache(sapMaterialNumber)),

                    HdNumber = NormalizeSharePointValue(Get(row, "HD číslo")),
                    SampleLocation = NormalizeSharePointValue(Get(row, "Umístění vzorků")),
                    BoardLocation = NormalizeSharePointValue(Get(row, "Umístění prkna")),
                    GaugeLocation = NormalizeSharePointValue(Get(row, "Umístění měrky")),
                    HasGauge = GetBool(row, "MěrkaBool"),
                    HasComplaint = GetBool(row, "Reklamace"),
                    SamplesOnCamera = GetBool(row, "Vzorky na kameru"),
                    Notes = NormalizeText(Get(row, "Poznámky")),
                    Tasks = tasks,
                    ImportedAt = DateTime.Now,
                    SourceFilePath = filePath,

                    Metadata = new QualityRecordMetadata
                    {
                        CreatedBy = "IMPORT",
                        CreatedAt = DateTime.Now,
                        ModifiedBy = string.Empty,
                        ModifiedAt = null
                    }
                });
            }

            printVersions = printVersions
                .GroupBy(item => item.FullPrintVersionNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.FullPrintVersionNumber)
                .ToList();

            _repository.SavePrintVersions(printVersions);

            return QualityExcelImportResult.Ok(
                $"Import tiskových verzí dokončen. Importováno: {printVersions.Count}",
                printVersions.Count,
                warnings);
        }
        catch (Exception ex)
        {
            return QualityExcelImportResult.Fail(
                $"Import tiskových verzí se nepodařil.\n\n{ex.Message}");
        }
    }

    public QualityExcelImportResult ImportOrders(string filePath)
    {
        try
        {
            var warnings = new List<string>();
            var rows = ReadRows(filePath);

            var orders = new List<QualityOrder>();

            foreach (var row in rows)
            {
                var orderNumber = NormalizeText(Get(row, "ZakázkaNr"));

                if (string.IsNullOrWhiteSpace(orderNumber))
                {
                    orderNumber = NormalizeText(Get(row, "Title"));
                }

                if (string.IsNullOrWhiteSpace(orderNumber))
                {
                    continue;
                }

                orders.Add(new QualityOrder
                {
                    OrderNumber = orderNumber,
                    PrintVersionNumber = NormalizeSharePointValue(Get(row, "Artikl.TiskováVerze")),
                    SapMaterialNumber = NormalizeSapNumber(Get(row, "Artikl.TiskováVerze:SAP")),
                    Machine = NormalizeSharePointValue(Get(row, "Stroje")),
                    Released = GetBool(row, "Uvolněno"),
                    ProductionStart = GetDate(row, "Datum od"),
                    ProductionEnd = GetDate(row, "Datum do"),
                    OrderedQuantity = GetInt(row, "ZakázkaNaMnožství"),
                    ProducedQuantity = GetInt(row, "Celkem odbaveno kusů"),
                    LabOrderNumber = NormalizeSharePointValue(Get(row, "Číslo lab. zakázky")),
                    LorealLabOrder = NormalizeSharePointValue(Get(row, "LorealLabZak")),
                    Loreal = GetBool(row, "Loreal"),
                    SortingInHd = GetBool(row, "Třídění v HD"),
                    StaysInHd = GetBool(row, "Zůstává v HD"),
                    QualityClass = NormalizeSharePointValue(Get(row, "TřídaKvality")),
                    SortingNumber = NormalizeSharePointValue(Get(row, "TřídícíČíslo")),
                    ColorType = NormalizeSharePointValue(Get(row, "Typ barvy")),
                    Notes = NormalizeSharePointValue(Get(row, "Poznámky")),
                    DefectReport = NormalizeSharePointValue(Get(row, "Hlášení závady (QT)")),
                    Finished = GetBool(row, "Ukončeno"),
                    CreatedAt = GetDate(row, "Erstellt"),
                    CreatedBy = NormalizeSharePointValue(Get(row, "Erstellt von")),
                    ImportedAt = DateTime.Now,
                    SourceFilePath = filePath,

                    Metadata = new QualityRecordMetadata
                    {
                        CreatedBy = "IMPORT",
                        CreatedAt = DateTime.Now,
                        ModifiedBy = string.Empty,
                        ModifiedAt = null
                    }
                });
            }

            orders = orders
                .GroupBy(item => item.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.OrderNumber)
                .ToList();

            _repository.SaveOrders(orders);

            return QualityExcelImportResult.Ok(
                $"Import zakázek dokončen. Importováno: {orders.Count}",
                orders.Count,
                warnings);
        }
        catch (Exception ex)
        {
            return QualityExcelImportResult.Fail(
                $"Import zakázek se nepodařil.\n\n{ex.Message}");
        }
    }

    private static List<Dictionary<string, string>> ReadRows(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);

        var worksheet = workbook.Worksheets.First();

        var headerRow = worksheet.Row(1);

        var headers = headerRow
            .CellsUsed()
            .Select(cell => new
            {
                Name = NormalizeHeader(cell.GetString()),
                ColumnNumber = cell.Address.ColumnNumber
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().ColumnNumber,
                StringComparer.OrdinalIgnoreCase);

        var result = new List<Dictionary<string, string>>();

        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var rowNumber = 2; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in headers)
            {
                var cell = row.Cell(header.Value);
                values[header.Key] = ReadCellText(cell);
            }

            if (values.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            result.Add(values);
        }

        return result;
    }

    private static string Get(Dictionary<string, string> row, string columnName)
    {
        var normalized = NormalizeHeader(columnName);

        return row.TryGetValue(normalized, out var value)
            ? value
            : string.Empty;
    }

    private static string NormalizeHeader(string? value)
    {
        return NormalizeText(value)
            .Replace("\u00A0", " ")
            .Trim();
    }

    private static string ReadCellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return cell.GetFormattedString().Trim();
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeSapNumber(string? value)
    {
        var text = NormalizeText(value);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.Replace(",", ".");

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }

    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static bool GetBool(Dictionary<string, string> row, string columnName)
    {
        var text = NormalizeText(Get(row, columnName));

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("ano", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static int? GetInt(Dictionary<string, string> row, string columnName)
    {
        var text = NormalizeText(Get(row, columnName));

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Replace(" ", string.Empty).Replace(",", ".");

        if (decimal.TryParse(
                text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var decimalValue))
        {
            return Convert.ToInt32(decimalValue);
        }

        return null;
    }

    private static DateTime? GetDate(Dictionary<string, string> row, string columnName)
    {
        var text = NormalizeText(Get(row, columnName));

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Replace(",", ".");

        if (double.TryParse(
                text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var serial))
        {
            try
            {
                return DateTime.FromOADate(serial);
            }
            catch
            {
                return null;
            }
        }

        if (DateTime.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string NormalizeSharePointValue(string? value)
    {
        var text = NormalizeText(value);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (!text.Contains(";#"))
        {
            return text;
        }

        var parts = text
            .Split(new[] { ";#" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(", ", parts);
    }

    private string GetQualityDecorationNameFromSapCache(string sapMaterialNumber)
    {
        var decorationChain = GetDecorationCodeFromSapCache(sapMaterialNumber);

        if (string.IsNullOrWhiteSpace(decorationChain))
        {
            return string.Empty;
        }

        var lastStep = GetLastDecorationStep(decorationChain);

        if (string.IsNullOrWhiteSpace(lastStep))
        {
            return string.Empty;
        }

        return _decorationRuleService is not null
            ? _decorationRuleService.GetName(lastStep)
            : lastStep;
    }

    private static string GetLastDecorationStep(string decorationChain)
    {
        var lastStep = decorationChain
            .Where(char.IsLetterOrDigit)
            .LastOrDefault();

        return lastStep == default
            ? string.Empty
            : lastStep.ToString();
    }
}