namespace DMS.Integration.Mes.Reporting.Models;

public sealed class MesCounterRecord
{
    public string WorkcenterCode { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public string CounterName { get; init; } = string.Empty;

    public string CounterDescription { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; }

    public decimal Value { get; init; }

    public string CustomText { get; init; } = string.Empty;
}
