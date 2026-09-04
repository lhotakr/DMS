using ClosedXML.Excel;
using System.Globalization;

namespace DMS.Core.Quality;

public sealed class QualityCustomerExcelImportService
{
    private readonly JsonQualityRepository _repository;

    public QualityCustomerExcelImportService(
        JsonQualityRepository repository)
    {
        _repository = repository
            ?? throw new ArgumentNullException(nameof(repository));
    }

    public QualityCustomerImportResult Import(
        string filePath)
    {
        var result = new QualityCustomerImportResult();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            result.ErrorCount++;
            result.Messages.Add(
                "Nebyla zadána cesta k Excel souboru.");

            return result;
        }

        if (!File.Exists(filePath))
        {
            result.ErrorCount++;
            result.Messages.Add(
                $"Soubor nebyl nalezen: {filePath}");

            return result;
        }

        try
        {
            var importedRows = ReadRows(filePath, result);

            var existingCustomers = _repository
                .LoadCustomers()
                .ToList();

            var existingBySourceId = existingCustomers
                .Where(customer => customer.SourceId > 0)
                .GroupBy(customer => customer.SourceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());

            var existingByName = existingCustomers
                .Where(customer =>
                    !string.IsNullOrWhiteSpace(customer.Name))
                .GroupBy(
                    customer => NormalizeKey(customer.Name),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

            var finalCustomers = existingCustomers.ToList();

            foreach (var imported in importedRows)
            {
                var existing = FindExistingCustomer(
                    imported,
                    existingBySourceId,
                    existingByName);

                if (existing is null)
                {
                    finalCustomers.Add(imported);

                    result.AddedCount++;
                    continue;
                }

                var index = finalCustomers.IndexOf(existing);

                if (index < 0)
                {
                    result.SkippedCount++;
                    continue;
                }

                finalCustomers[index] = Merge(
                    existing,
                    imported);

                result.UpdatedCount++;
            }

            var deduplicated = finalCustomers
                .Where(customer =>
                    !string.IsNullOrWhiteSpace(customer.Name))
                .GroupBy(
                    customer => BuildUniqueKey(customer),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(customer => customer.Name)
                .ToList();

            _repository.SaveCustomers(deduplicated);

            result.ImportedCount = importedRows.Count;

            result.Messages.Insert(
                0,
                $"Import zákazníků dokončen. " +
                $"Načteno {result.SourceRows}, " +
                $"přidáno {result.AddedCount}, " +
                $"aktualizováno {result.UpdatedCount}, " +
                $"uloženo {deduplicated.Count} zákazníků.");
        }
        catch (Exception ex)
        {
            result.ErrorCount++;

            result.Messages.Add(
                $"Import zákazníků selhal: {ex.Message}");
        }

        return result;
    }

    private static List<QualityCustomer> ReadRows(
        string filePath,
        QualityCustomerImportResult result)
    {
        using var workbook = new XLWorkbook(filePath);

        var worksheet = workbook.Worksheets.FirstOrDefault();

        if (worksheet is null)
        {
            throw new InvalidOperationException(
                "Excel soubor neobsahuje žádný list.");
        }

        var usedRange = worksheet.RangeUsed();

        if (usedRange is null)
        {
            return new List<QualityCustomer>();
        }

        var headerRow = usedRange.FirstRowUsed();

        if (headerRow is null)
        {
            return new List<QualityCustomer>();
        }

        var headers = headerRow
            .CellsUsed()
            .ToDictionary(
                cell => NormalizeHeader(cell.GetString()),
                cell => cell.Address.ColumnNumber,
                StringComparer.OrdinalIgnoreCase);

        var idColumn = FindColumn(
            headers,
            "id");

        var nameColumn = FindColumn(
            headers,
            "title",
            "titel",
            "nazev",
            "název",
            "customer",
            "zakaznik",
            "zákazník");

        var lorealColumn = FindColumn(
            headers,
            "jeloreal",
            "isloréal",
            "isloreal",
            "loreal",
            "l'oréal");

        if (nameColumn is null)
        {
            throw new InvalidOperationException(
                "V exportu nebyl nalezen sloupec Title, Titel nebo Název zákazníka.");
        }

        var customers = new List<QualityCustomer>();

        foreach (var row in usedRange.RowsUsed().Skip(1))
        {
            result.SourceRows++;

            try
            {
                var name = NormalizeText(
                    row.Cell(nameColumn.Value).GetString());

                if (string.IsNullOrWhiteSpace(name))
                {
                    result.SkippedCount++;
                    continue;
                }

                var sourceId = idColumn.HasValue
                    ? ReadInt(row.Cell(idColumn.Value))
                    : 0;

                var isLoreal = lorealColumn.HasValue &&
                               ReadBool(
                                   row.Cell(lorealColumn.Value));

                customers.Add(new QualityCustomer
                {
                    Code = sourceId > 0
                        ? sourceId.ToString(
                            CultureInfo.InvariantCulture)
                        : string.Empty,

                    Name = name,
                    IsActive = true,
                    IsLoreal = isLoreal,
                    SourceId = sourceId
                });
            }
            catch (Exception ex)
            {
                result.ErrorCount++;

                result.Messages.Add(
                    $"Řádek {row.RowNumber()}: {ex.Message}");
            }
        }

        return customers;
    }

    private static QualityCustomer? FindExistingCustomer(
        QualityCustomer imported,
        IReadOnlyDictionary<int, QualityCustomer> bySourceId,
        IReadOnlyDictionary<string, QualityCustomer> byName)
    {
        if (imported.SourceId > 0 &&
            bySourceId.TryGetValue(
                imported.SourceId,
                out var byId))
        {
            return byId;
        }

        var nameKey = NormalizeKey(imported.Name);

        return byName.TryGetValue(nameKey, out var byCustomerName)
            ? byCustomerName
            : null;
    }

    private static QualityCustomer Merge(
        QualityCustomer existing,
        QualityCustomer imported)
    {
        return new QualityCustomer
        {
            Code = imported.Code,
            Name = imported.Name,

            // Ruční deaktivaci při opakovaném importu zachováme.
            IsActive = existing.IsActive,

            IsLoreal = imported.IsLoreal,
            SourceId = imported.SourceId
        };
    }

    private static int? FindColumn(
        IReadOnlyDictionary<string, int> headers,
        params string[] possibleNames)
    {
        foreach (var possibleName in possibleNames)
        {
            var normalized = NormalizeHeader(possibleName);

            if (headers.TryGetValue(
                    normalized,
                    out var column))
            {
                return column;
            }
        }

        return null;
    }

    private static int ReadInt(IXLCell cell)
    {
        if (cell.TryGetValue<int>(out var numeric))
        {
            return numeric;
        }

        var text = NormalizeText(cell.GetString());

        return int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : 0;
    }

    private static bool ReadBool(IXLCell cell)
    {
        if (cell.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        var text = NormalizeKey(cell.GetString());

        return text switch
        {
            "true" => true,
            "ano" => true,
            "yes" => true,
            "1" => true,
            "x" => true,
            _ => false
        };
    }

    private static string BuildUniqueKey(
        QualityCustomer customer)
    {
        return customer.SourceId > 0
            ? $"ID:{customer.SourceId}"
            : $"NAME:{NormalizeKey(customer.Name)}";
    }

    private static string NormalizeHeader(
        string? value)
    {
        return NormalizeKey(value)
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
    }

    private static string NormalizeKey(
        string? value)
    {
        return NormalizeText(value)
            .ToLowerInvariant();
    }

    private static string NormalizeText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace(";#", ", ")
            .Trim();
    }
}