namespace DMS.Integration.Mes.Reporting.Models;

public sealed class MesProductionRecord
{
    public DateTime Starttime { get; init; }

    public DateTime? Endtime { get; init; }

    public string WorkcenterCode { get; init; } = string.Empty;

    public string WorkcenterDescription { get; init; } = string.Empty;

    public string PlantName { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;

    public string OperationCode { get; init; } = string.Empty;

    public decimal? OperationQuantity { get; init; }

    public string ProductCode { get; init; } = string.Empty;

    public string ProductDescription { get; init; } = string.Empty;

    public decimal? OrderQuantity { get; init; }

    public decimal? PerformanceTotal { get; init; }

    public decimal? PerformanceGood { get; init; }

    public decimal? PerformanceBad { get; init; }

    public decimal? PerformanceRework { get; init; }

    public decimal? DurationUtilization { get; init; }

    public decimal? DurationDown { get; init; }

    public string IntervalState =>
        Endtime.HasValue
            ? "Closed"
            : "Open";
}
