namespace DMS.Core.Sap;

public sealed class SapBom
{
    public string MaterialNumber { get; set; } = string.Empty;
    public string Plant { get; set; } = string.Empty;
    public string BomUsage { get; set; } = string.Empty;      // STLAN
    public string BomNumber { get; set; } = string.Empty;     // STLNR
    public string Alternative { get; set; } = string.Empty;   // STLAL
    public decimal? BaseQuantity { get; set; }                // BMENG
    public string BaseUnit { get; set; } = string.Empty;       // BMEIN
    public string BomMeaning { get; set; } = string.Empty;     // 9200/2000 význam pro DMS
    public List<SapBomItem> Items { get; set; } = new();
    public DateTime ImportedAt { get; set; } = DateTime.Now;
}

public sealed class SapBomItem
{
    public string Position { get; set; } = string.Empty;          // POSNR
    public string ComponentNumber { get; set; } = string.Empty;   // IDNRK
    public decimal? Quantity { get; set; }                        // MENGE
    public string Unit { get; set; } = string.Empty;              // MEINS
    public string ItemCategory { get; set; } = string.Empty;      // POSTP
    public string ItemText { get; set; } = string.Empty;
    public decimal? ScrapPercent { get; set; }                    // STPO-AUSCH
    public bool IsFixedQuantity { get; set; }                     // STPO-FMENG
    public string ComponentDescription { get; set; } = string.Empty;
    public string ComponentKind { get; set; } = string.Empty;
    public string NodeNumber { get; set; } = string.Empty; // STPO-STLKN
    public string Counter { get; set; } = string.Empty;    // případně STASZ / interní čítač
}