namespace DMS.Core.Quality.Import;

public sealed class QualityExcelImportResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int ImportedCount { get; init; }

    public List<string> Warnings { get; init; } = new();

    public static QualityExcelImportResult Ok(string message, int importedCount, List<string>? warnings = null)
    {
        return new QualityExcelImportResult
        {
            Success = true,
            Message = message,
            ImportedCount = importedCount,
            Warnings = warnings ?? new List<string>()
        };
    }

    public static QualityExcelImportResult Fail(string message, List<string>? warnings = null)
    {
        return new QualityExcelImportResult
        {
            Success = false,
            Message = message,
            ImportedCount = 0,
            Warnings = warnings ?? new List<string>()
        };
    }
}