namespace DMS.Core.Sap;

public sealed class SapTechnicalVariantSummary
{
    public string Plant { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;

    public List<SapBom> Boms { get; init; } = new();
    public List<SapRouting> Routings { get; init; } = new();

    public string Title
    {
        get
        {
            var alternativeText = string.IsNullOrWhiteSpace(Alternative)
                ? "bez alternativy"
                : Alternative;

            return $"Závod {Plant} / Alternativa {alternativeText}";
        }
    }
}