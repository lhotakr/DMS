using System;

namespace DMS.Integration.Mes.Orders;

/// <summary>
/// Server-side ORD10 advanced search.
/// Null/empty values mean "do not filter".
/// </summary>
public sealed class MesOrderAdvancedSearchCriteria
{
    // General data
    public string OrderCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDesignation { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;
    public string OrderDescription { get; init; } = string.Empty;

    // Extended / routing
    public string CostCenter { get; init; } = string.Empty;

    // References
    public string SapArticleNumber { get; init; } = string.Empty;
    public string CustomerOrderCode { get; init; } = string.Empty;
    public string CustomerCode { get; init; } = string.Empty;

    // Dates
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedToExclusive { get; init; }
    public DateTime? PlannedStartFrom { get; init; }
    public DateTime? PlannedStartToExclusive { get; init; }
    public DateTime? PlannedEndFrom { get; init; }
    public DateTime? PlannedEndToExclusive { get; init; }
    public DateTime? ActualStartFrom { get; init; }
    public DateTime? ActualStartToExclusive { get; init; }
    public DateTime? ActualEndFrom { get; init; }
    public DateTime? ActualEndToExclusive { get; init; }

    // Quantities
    public decimal? TargetQuantityMin { get; init; }
    public decimal? TargetQuantityMax { get; init; }
    public decimal? FinishedQuantityMin { get; init; }
    public decimal? FinishedQuantityMax { get; init; }
    public decimal? ScrapQuantityMin { get; init; }
    public decimal? ScrapQuantityMax { get; init; }
    public decimal? ProgressPercentMin { get; init; }
    public decimal? ProgressPercentMax { get; init; }

    // FASTEC state axes
    public int? ArchiveStatus { get; init; }
    public int? FailureStatus { get; init; }
    public int? GeneralStatus { get; init; }
    public int? PlanningStatus { get; init; }
    public int? PlanningFixStatus { get; init; }
    public int? ProductionStatus { get; init; }

    public int MaxRows { get; init; } = 1000;
}
