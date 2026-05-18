namespace DMS.Core.Sap;

public sealed class SapMaterialNumberRange
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string MaterialKind { get; init; } = string.Empty;
    public string TransactionPrefix { get; init; } = string.Empty;
    public bool IsImported { get; init; }
    public string Description { get; init; } = string.Empty;
}