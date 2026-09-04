namespace DMS.Desktop.Models;

public sealed class ArticleOperation
{
    public string OperationNumber { get; set; } = "0010";
    // SAP-like číslo operace: 0010, 0020, 0030...

    public string OperationTypeCode { get; set; } = string.Empty;
    // B, D, E, K, N, P, V, X...

    public string WorkCenterGroupCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal? ShiftStandardQuantity { get; set; }

    public string ShiftStandardUnit { get; set; } = "ks/směna";

    public decimal? ExpectedScrapPercent { get; set; }

    public decimal? ExpectedScrapQuantity { get; set; }

    public string Note { get; set; } = string.Empty;
}