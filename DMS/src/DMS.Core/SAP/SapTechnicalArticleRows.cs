namespace DMS.Core.Sap;

public sealed class SapTechnicalRoutingOperationRow
{
    public string Plant { get; init; } = string.Empty;
    public string OperationNumber { get; init; } = string.Empty;

    public string WorkCenterDisplay { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string ScrapPercent { get; init; } = string.Empty;
    public string SetupTime { get; init; } = string.Empty;
    public string ShiftTakt { get; init; } = string.Empty;
    public string PersonnelCount { get; init; } = string.Empty;
    public string InfoRecord { get; init; } = string.Empty;

    public bool HasWarning { get; init; }
}

public sealed class SapTechnicalBomItemRow
{
    public string Plant { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;

    public string ComponentDescription { get; init; } = string.Empty;
    public string ComponentNumber { get; init; } = string.Empty;

    public string Quantity { get; init; } = string.Empty;
    public string ScrapPercent { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string IsFixedQuantity { get; init; } = string.Empty;

    public bool HasWarning { get; init; }
}