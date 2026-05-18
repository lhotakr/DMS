namespace DMS.Desktop.Models;

public sealed class ArticleSelectionRow
{
    public string SapNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string OldNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public string MaterialKind { get; init; } = string.Empty;
    public string TransactionPrefix { get; init; } = string.Empty;

    public string Decoration { get; init; } = string.Empty;
    public string ExtraInfo { get; init; } = string.Empty;
}