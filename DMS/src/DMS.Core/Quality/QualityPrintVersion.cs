namespace DMS.Core.Quality;

public sealed class QualityPrintVersion
{
    public QualityRecordMetadata Metadata { get; init; } = new();
    public string FullPrintVersionNumber { get; init; } = string.Empty;

    public string LegacyArticleNumber { get; init; } = string.Empty;

    public string GlassType { get; init; } = string.Empty;

    public string VersionNumber { get; init; } = string.Empty;

    public string SapMaterialNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Customer { get; init; } = string.Empty;

    public string ColorType { get; init; } = string.Empty;

    public string GlassTreatment { get; init; } = string.Empty;
    public string DecorationCode { get; init; } = string.Empty;

    public string HdNumber { get; init; } = string.Empty;

    public string SampleLocation { get; init; } = string.Empty;

    public string BoardLocation { get; init; } = string.Empty;

    public string GaugeLocation { get; init; } = string.Empty;

    public bool HasGauge { get; init; }

    public bool HasComplaint { get; init; }

    public bool SamplesOnCamera { get; init; }

    public string Notes { get; init; } = string.Empty;

    public List<QualityTask> Tasks { get; init; } = new();

    public DateTime ImportedAt { get; init; } = DateTime.Now;

    public string SourceFilePath { get; init; } = string.Empty;

    public string QualityClass { get; set; } = string.Empty;

}