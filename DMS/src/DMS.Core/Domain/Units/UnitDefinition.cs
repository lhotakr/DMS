namespace DMS.Core.Domain.Units;

public sealed class UnitDefinition
{
    public Guid UnitDefinitionId { get; set; } = Guid.NewGuid();
    public Guid UnitDimensionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ScaleToBase { get; set; } = 1m;
    public decimal OffsetToBase { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
