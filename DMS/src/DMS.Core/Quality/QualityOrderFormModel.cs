namespace DMS.Core.Quality;

public sealed class QualityOrderFormModel
{
    public string Query { get; set; } = string.Empty;

    public bool IsCreateMode { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public string PrintVersionNumber { get; set; } = string.Empty;

    public string SapMaterialNumber { get; set; } = string.Empty;

    public string Machine { get; set; } = string.Empty;

    public string ColorType { get; set; } = string.Empty;

    public DateTime? ProductionStart { get; set; }

    public DateTime? ProductionEnd { get; set; }

    public int? OrderedQuantity { get; set; }

    public int? ProducedQuantity { get; set; }

    public string QualityClass { get; set; } = string.Empty;

    public string LabOrderNumber { get; set; } = string.Empty;

    public string LorealLabOrder { get; set; } = string.Empty;

    public bool Loreal { get; set; }

    public bool SortingInHd { get; set; }

    public bool StaysInHd { get; set; }

    public bool Released { get; set; }

    public bool Finished { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string Customer { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string SampleLocation { get; set; } = string.Empty;

    public string BoardLocation { get; set; } = string.Empty;

    public string HdNumber { get; set; } = string.Empty;

    public string GaugeLocation { get; set; } = string.Empty;

    public bool HasGauge { get; set; }

    public bool SamplesOnCamera { get; set; }

    public bool HasComplaint { get; set; }

    public bool AllTasksCompleted { get; set; }

    public string PrintVersionNotes { get; set; } = string.Empty;

    public string TaskSummary { get; set; } = string.Empty;

    public string LegacyArticleNumber { get; set; } = string.Empty;

    public string ArticleTitle { get; set; } = string.Empty;

    public string ArticleImportantInfo { get; set; } = string.Empty;

    public string ArticleNotes { get; set; } = string.Empty;

    public List<QualityTask> OpenTasks { get; set; } = new();

    public string OpenTasksText { get; set; } = string.Empty;

    public string ScheduleStatusCode { get; set; } = string.Empty;

    public string ReleaseStatusCode { get; set; } = string.Empty;

    public QualityOrder? OriginalOrder { get; set; }

    public QualityPrintVersion? SourcePrintVersion { get; set; }
}
