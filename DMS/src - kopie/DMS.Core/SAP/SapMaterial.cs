namespace DMS.Core.Sap;

public sealed class SapMaterial
{
    public string MaterialNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? OldMaterialNumber { get; init; }
    public string? MaterialStatus { get; init; }
    public PackagingInfo? PackagingInfo { get; init; }
    public string MaterialKind { get; init; } = string.Empty;
    public string TransactionPrefix { get; init; } = string.Empty;
    public string? ToolFixtureKind { get; init; }
    public GlassArticleTextInfo? GlassInfo { get; init; }
    public DateTime ImportedAt { get; init; } = DateTime.Now;
}