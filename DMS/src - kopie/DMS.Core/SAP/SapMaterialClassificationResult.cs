namespace DMS.Core.Sap;

public sealed class SapMaterialClassificationResult
{
    public string MaterialNumber { get; init; } = string.Empty;

    public string MaterialKind { get; init; } = string.Empty;

    public bool Import { get; init; }

    public string? SapNumberPrefix { get; init; }

    public string? MatchedRuleName { get; init; }
}