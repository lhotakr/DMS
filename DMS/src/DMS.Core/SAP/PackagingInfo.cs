namespace DMS.Core.Sap;

public sealed class PackagingInfo
{
    public string PackagingKind { get; init; } = string.Empty;

    public string? LinkedArticleSapNumber { get; init; }

    public string? LinkedArticleOldNumber { get; init; }
}