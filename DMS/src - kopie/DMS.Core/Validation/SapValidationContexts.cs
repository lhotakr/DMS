namespace DMS.Core.Sap.Validation;

public sealed class SapBomHeaderValidationContext
{
    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;
    public string BomUsage { get; init; } = string.Empty;

    public decimal? BaseQuantity { get; init; }
    public string BaseUnit { get; init; } = string.Empty;
}

public sealed class SapBomItemValidationContext
{
    public string ArticleNumber { get; init; } = string.Empty;

    public string Plant { get; init; } = string.Empty;
    public string BomNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;

    public string Position { get; init; } = string.Empty;
    public string ItemCategory { get; init; } = string.Empty;

    public string ComponentNumber { get; init; } = string.Empty;
    public string ComponentDescription { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;

    public decimal? ScrapPercent { get; init; }
    public bool IsFixedQuantity { get; init; }

    public bool IsTextItem { get; init; }

    public bool IsSelfComponent { get; init; }
    public bool IsSortingAlternative { get; init; }
}

public sealed class SapRoutingOperationValidationContext
{
    public string Plant { get; init; } = string.Empty;
    public string GroupNumber { get; init; } = string.Empty;
    public string Alternative { get; init; } = string.Empty;

    public string OperationNumber { get; init; } = string.Empty;

    public string WorkCenter { get; init; } = string.Empty;
    public string WorkCenterText { get; init; } = string.Empty;

    public string ControlKey { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public decimal? BaseQuantity { get; init; }
    public string BaseUnit { get; init; } = string.Empty;

    public decimal? Vgw01 { get; init; }
    public decimal? Vgw03 { get; init; }
    public decimal? Vgw04 { get; init; }

    public decimal? ScrapPercent { get; init; }

    public string InfoRecord { get; init; } = string.Empty;

    public bool IsFirstOperation { get; init; }
    public bool IsLastOperation { get; init; }
}

public sealed class SapCrossPlantValidationContext
{
    public string ArticleNumber { get; init; } = string.Empty;

    public decimal? LastZpp2Scrap2000 { get; init; }

    public string BomNumber9200 { get; init; } = string.Empty;
    public string Position9200 { get; init; } = string.Empty;
    public string ComponentNumber9200 { get; init; } = string.Empty;
    public decimal? ComponentScrap9200 { get; init; }
    public string BomAlternative9200 { get; init; } = string.Empty;
    public bool IsSortingAlternative9200 { get; init; }
}

public sealed class SapArticleSummaryValidationContext
{
    public string ArticleNumber { get; init; } = string.Empty;

    public bool MaterialFound { get; init; }

    public int Bom9200Count { get; init; }
    public int Bom2000Count { get; init; }

    public int Routing9200Count { get; init; }
    public int Routing2000Count { get; init; }
}

public sealed class SapDecorationValidationContext
{
    public string ArticleNumber { get; init; } = string.Empty;
    public string BomNumber9200 { get; init; } = string.Empty;
    public string Position9200 { get; init; } = string.Empty;
    public string ArticleDecorationCode { get; init; } = string.Empty;
    public string ComponentDecorationCode { get; init; } = string.Empty;
    public string DecorationDifference { get; init; } = string.Empty;
    public bool IsDecorationDifferenceCoveredByRouting2000 { get; init; }
    public string Routing2000Technologies { get; init; } = string.Empty;
    public string BomAlternative9200 { get; init; } = string.Empty;
    public bool IsSortingAlternative9200 { get; init; }
}