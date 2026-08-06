namespace DMS.Core.Quality;

public sealed class QualityOrder
{
    public QualityRecordMetadata Metadata { get; init; } = new();
    public string OrderNumber { get; init; } = string.Empty;

    public string PrintVersionNumber { get; init; } = string.Empty;

    public string SapMaterialNumber { get; init; } = string.Empty;

    public string Machine { get; init; } = string.Empty;

    public bool Released { get; init; }

    public DateTime? ProductionStart { get; init; }

    public DateTime? ProductionEnd { get; init; }

    public int? OrderedQuantity { get; init; }

    public int? ProducedQuantity { get; init; }

    public string LabOrderNumber { get; init; } = string.Empty;

    public string LorealLabOrder { get; init; } = string.Empty;

    public bool Loreal { get; init; }

    public bool SortingInHd { get; init; }

    public bool StaysInHd { get; init; }

    public string QualityClass { get; init; } = string.Empty;

    public string SortingNumber { get; init; } = string.Empty;

    public string ColorType { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string DefectReport { get; init; } = string.Empty;

    public bool TesaTest { get; init; }

    public bool AcetoneTest { get; init; }

    public bool GridTest { get; init; }

    public bool VisualCheck { get; init; }

    public bool Approved { get; init; }

    public string ReleaseNotes { get; init; } = string.Empty;

    public string ReleasedBy { get; init; } = string.Empty;

    public DateTime? ReleasedAt { get; init; }

    public string BlockedBy { get; init; } = string.Empty;

    public DateTime? BlockedAt { get; init; }

    public bool Finished { get; init; }

    public DateTime ImportedAt { get; init; } = DateTime.Now;

    public DateTime? CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string SourceFilePath { get; init; } = string.Empty;
}