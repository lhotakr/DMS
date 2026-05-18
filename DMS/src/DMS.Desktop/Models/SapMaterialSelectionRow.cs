namespace DMS.Desktop.Models;

public sealed class SapMaterialSelectionRow
{
    public string MaterialNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string OldMaterialNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string MaterialKind { get; init; } = string.Empty;
    public string TransactionPrefix { get; init; } = string.Empty;
    public string ExtraInfo { get; init; } = string.Empty;
}