namespace DMS.Core.Quality;

public sealed class QualityOrderListRow
{
    public string OrderNumber { get; set; } = string.Empty;

    public string PrintVersionNumber { get; set; } = string.Empty;

    public string SapMaterialNumber { get; set; } = string.Empty;

    public string Customer { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ArticleTitle { get; set; } = string.Empty;

    public string OpenTasksText { get; set; } = string.Empty;

    public int OpenTaskCount { get; set; }

    public string Machine { get; set; } = string.Empty;

    public string ColorType { get; set; } = string.Empty;

    public string ProductionStartText { get; set; } = string.Empty;

    public string ProductionEndText { get; set; } = string.Empty;

    public int? OrderedQuantity { get; set; }

    public int? ProducedQuantity { get; set; }

    public string QualityClass { get; set; } = string.Empty;

    public string LorealText { get; set; } = string.Empty;

    public string ReleasedText { get; set; } = string.Empty;

    public string BlockedText { get; set; } = string.Empty;

    public string ReleaseIcon { get; set; } = string.Empty;

    public string ReleaseStatusCode { get; set; } = string.Empty;

    public string ScheduleStatusCode { get; set; } = string.Empty;

    public string ScheduleStatusText { get; set; } = string.Empty;

    public string ScheduleSemaphore { get; set; } = string.Empty;

    public string FinishedText { get; set; } = string.Empty;

    public DateTime? CreatedAtDate { get; set; }

    public string CreatedAtText { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public bool IsBlocked => !Source.Released;

    public bool IsReleased => Source.Released;

    public bool IsFinished => Source.ProductionStart.HasValue && Source.ProductionEnd.HasValue;

    public bool IsUnplanned => !Source.ProductionStart.HasValue;

    public bool IsScheduled => Source.ProductionStart.HasValue && !Source.ProductionEnd.HasValue;

    public QualityOrder Source { get; set; } = null!;
}
