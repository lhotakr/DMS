namespace DMS.Core.Sap;

public sealed class SapMaterialUsageOverview
{
    public string MaterialNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string OldMaterialNumber { get; init; } = string.Empty;
    public string MaterialKind { get; init; } = string.Empty;
    public string MaterialStatus { get; init; } = string.Empty;

    public SapMaterial? Material { get; init; }

    public List<SapMaterialUsedAsComponentRow> UsedAsComponent { get; init; } = new();
    public List<SapMaterialOwnBomVariant> OwnBomVariants { get; init; } = new();
    public List<string> Messages { get; init; } = new();
}

public sealed class SapMaterialUsedAsComponentRow
{
    public string ParentMaterialNumber { get; init; } = string.Empty;
    public string ParentDescription { get; init; } = string.Empty;
    public string ParentMaterialKind { get; init; } = string.Empty;

    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;
}

public sealed class SapMaterialOwnBomVariant
{
    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;
    public string BomUsage { get; init; } = string.Empty;

    public decimal? BaseQuantity { get; init; }
    public string BaseUnit { get; init; } = string.Empty;

    public List<SapMaterialOwnBomItemRow> Items { get; init; } = new();
}

public sealed class SapMaterialOwnBomItemRow
{
    public string Position { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;
    public string ComponentNumber { get; init; } = string.Empty;
    public string ComponentDescription { get; init; } = string.Empty;
    public string ComponentKind { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
    public bool IsFixedQuantity { get; init; }
    public decimal? ScrapPercent { get; init; }
}