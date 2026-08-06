namespace DMS.Core.Domain.Units;

public sealed class MeasurementValue
{
    public decimal EnteredValue { get; set; }
    public string EnteredUnitCode { get; set; } = string.Empty;
    public decimal NormalizedValue { get; set; }
    public string NormalizedUnitCode { get; set; } = string.Empty;
    public int ConversionRevision { get; set; } = 1;
}
