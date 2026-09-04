namespace DMS.Core.Sap;

public sealed class GlassArticleTextInfo
{
    public string? MoldNumber { get; init; }
    public string? GlassTypeNumber { get; init; }
    public int? VolumeMl { get; init; }
    public string? DecorationChain { get; init; }
    public List<string> DecorationSteps { get; init; } = new();
    public string? RemainingDescription { get; init; }
}