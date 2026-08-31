namespace DMS.Integration.Mes.Reporting;

public sealed class MesReportFilter
{
    public DateTime From { get; set; }

    public DateTime To { get; set; }

    public string WorkcenterCode { get; set; } = string.Empty;

    public string OrderCode { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public int MaxRows { get; set; } = 5000;

    public void Normalize()
    {
        if (To <= From)
        {
            To = From.AddDays(1);
        }

        WorkcenterCode =
            WorkcenterCode?.Trim() ?? string.Empty;

        OrderCode =
            OrderCode?.Trim() ?? string.Empty;

        ProductCode =
            ProductCode?.Trim() ?? string.Empty;

        MaxRows =
            Math.Clamp(
                MaxRows,
                100,
                50000);
    }
}
