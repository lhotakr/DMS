namespace DMS.Core.Quality;

public sealed class QualityArticleCreateModel
{
    public string SapMaterialNumber { get; set; } = string.Empty;

    public string SapTitle { get; set; } = string.Empty;

    public string OldMaterialNumber { get; set; } = string.Empty;

    public string FullPrintVersionNumber { get; set; } = string.Empty;

    public string PrintVersionTitle { get; set; } = string.Empty;

    public string DecorationCode { get; set; } = string.Empty;

    public string Customer { get; set; } = string.Empty;

    public string ColorType { get; set; } = string.Empty;

    public string GlassTreatment { get; set; } = string.Empty;

    public string QualityClass { get; set; } = string.Empty;

    public string HdNumber { get; set; } = string.Empty;

    public string SampleLocation { get; set; } = string.Empty;

    public string BoardLocation { get; set; } = string.Empty;

    public string GaugeLocation { get; set; } = string.Empty;

    public bool HasGauge { get; set; }

    public bool HasComplaint { get; set; }

    public bool SamplesOnCamera { get; set; }

    public string ImportantInfo { get; set; } = string.Empty;

    public string ArticleNotes { get; set; } = string.Empty;

    public string PrintVersionNotes { get; set; } = string.Empty;
}