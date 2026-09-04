namespace DMS.Integration.Mes.Reporting.Models;

public sealed class MesStateRecord
{
    public string WorkcenterCode { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public DateTime Starttime { get; init; }

    public DateTime? Endtime { get; init; }

    public string StateName { get; init; } = string.Empty;

    public string StateDescription { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public bool IsSetup { get; init; }

    public bool IsBreak { get; init; }

    public bool IsCauselessFailure { get; init; }

    public string CustomText { get; init; } = string.Empty;

    public double DurationSeconds { get; init; }

    public double DurationMinutes =>
        DurationSeconds / 60d;
}
