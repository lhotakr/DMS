namespace DMS.Core.Sap;

public sealed class SapRouting
{
    public string MaterialNumber { get; set; } = string.Empty;
    public string Plant { get; set; } = string.Empty;

    public string TaskListType { get; set; } = string.Empty;    // PLNTY
    public string GroupNumber { get; set; } = string.Empty;     // PLNNR
    public string Alternative { get; set; } = string.Empty;     // PLNAL

    public string Description { get; set; } = string.Empty;     // PLKO-KTEXT
    public string Status { get; set; } = string.Empty;          // PLKO-STATU
    public string Usage { get; set; } = string.Empty;           // PLKO-VERWE

    public string RoutingMeaning { get; set; } = string.Empty;

    public List<SapRoutingOperation> Operations { get; set; } = new();

    public bool HasCriticalError { get; set; }
    public List<string> ValidationMessages { get; set; } = new();

    public DateTime ImportedAt { get; set; } = DateTime.Now;
}

public sealed class SapRoutingOperation
{
    public string OperationNumber { get; set; } = string.Empty;     // VORNR
    public string WorkCenterObjectId { get; set; } = string.Empty;  // ARBID

    // Doplníme později z CR05 / ruční mapy pracovišť.
    public string WorkCenter { get; set; } = string.Empty;
    public string WorkCenterText { get; set; } = string.Empty;

    public string ControlKey { get; set; } = string.Empty;          // STEUS
    public string Description { get; set; } = string.Empty;         // LTXA1

    public decimal? BaseQuantity { get; set; }                      // BMSCH
    public string BaseUnit { get; set; } = string.Empty;            // MEINH

    public decimal? Vgw01 { get; set; }                             // PLPOD-VGW01
    public string Vge01 { get; set; } = string.Empty;

    public decimal? Vgw03 { get; set; }                             // PLPOD-VGW03
    public string Vge03 { get; set; } = string.Empty;

    public decimal? Vgw04 { get; set; }                             // 2000 = počet lidí
    public string Vge04 { get; set; } = string.Empty;

    public string InfoRecord { get; set; } = string.Empty;          // INFNR

    public string OperationMeaning { get; set; } = string.Empty;
    public decimal? ScrapPercent { get; set; }          // zatím ručně/dodatečně, 2000
    public decimal? SetupTime { get; set; }             // plánovaná přestavba, pokud ji později doplníme
    public decimal? ShiftTakt { get; set; }             // vypočtené 7,5 * BMSCH
    public string NodeNumber { get; set; } = string.Empty; // PLPO-PLNKN
    public string Counter { get; set; } = string.Empty;    // PLPO-ZAEHL
}