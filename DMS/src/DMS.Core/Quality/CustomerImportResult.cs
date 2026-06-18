namespace DMS.Core.Quality;

public sealed class QualityCustomerImportResult
{
    public int SourceRows { get; set; }

    public int ImportedCount { get; set; }

    public int AddedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int ErrorCount { get; set; }

    public List<string> Messages { get; } = new();

    public bool Success => ErrorCount == 0;
}