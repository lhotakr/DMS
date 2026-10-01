namespace DMS.Desktop.Help;

public sealed class DmsHelpDocument
{
    public string TopicId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public string Markdown { get; init; } = string.Empty;
    public string Culture { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public bool IsGenerated { get; init; }
    public bool IsTransaction { get; init; }
    public string TransactionCode { get; init; } = string.Empty;
}
