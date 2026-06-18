namespace DMS.Core.Sap;

public sealed class SapTechnicalArticleSummary
{
    public string ArticleNumber { get; set; } = string.Empty;

    public SapMaterial? Material { get; set; }

    public List<SapBom> Boms9200 { get; set; } = new();
    public List<SapBom> Boms2000 { get; set; } = new();

    public List<SapRouting> Routings9200 { get; set; } = new();
    public List<SapRouting> Routings2000 { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
    public List<string> CriticalErrors { get; set; } = new();

    public bool HasCriticalError => CriticalErrors.Count > 0;
    public bool HasWarning => Warnings.Count > 0;
    public List<SapTechnicalVariantSummary> Variants { get; set; } = new();
    public string StatusText
    {
        get
        {
            if (HasCriticalError)
            {
                return "Kritický problém";
            }

            if (HasWarning)
            {
                return "Varování";
            }

            return "Připraveno";
        }
    }
}