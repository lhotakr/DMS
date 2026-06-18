namespace DMS.Core.Quality;

public sealed class QualityArticleEditModel
{
    public string Query { get; init; } = string.Empty;

    public string LegacyArticleNumber { get; set; } = string.Empty;

    public string ImportantInfo { get; set; } = string.Empty;

    public string ArticleNotes { get; set; } = string.Empty;

    public List<QualityPrintVersionEditModel> PrintVersions { get; init; } = new();
}

public sealed class QualityPrintVersionEditModel
{
    public string OriginalPrintVersionNumber { get; init; } = string.Empty;

    public string FullPrintVersionNumber { get; set; } = string.Empty;

    public string SapMaterialNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Customer { get; set; } = string.Empty;

    public string ColorType { get; set; } = string.Empty;

    public string GlassTreatment { get; set; } = string.Empty;

    public string DecorationCode { get; set; } = string.Empty;

    public string HdNumber { get; set; } = string.Empty;

    public string SampleLocation { get; set; } = string.Empty;

    public string BoardLocation { get; set; } = string.Empty;

    public string GaugeLocation { get; set; } = string.Empty;

    public bool HasGauge { get; set; }

    public bool HasComplaint { get; set; }

    public bool SamplesOnCamera { get; set; }

    public string Notes { get; set; } = string.Empty;

    public List<QualityTaskEditModel> Tasks { get; init; } = new();
    public string QualityClass { get; set; } = string.Empty;

    public override string ToString()
    {
        var title = string.IsNullOrWhiteSpace(Title)
            ? "-"
            : Title;

        return $"{FullPrintVersionNumber} | {SapMaterialNumber} | {title}";
    }
}