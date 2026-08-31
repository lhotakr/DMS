using System;
using System.Collections.Generic;

namespace DMS.Integration.Mes.Live;

public sealed class MesLiveWorkcenterRecord
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// FASTEC designation used for ordering / operator-facing display.
    /// In the current ana reporting snapshot this is mapped from ErpCode,
    /// because the analytical dimension does not expose a literal Designation column.
    /// </summary>
    public string Designation { get; init; } = string.Empty;

    /// <summary>
    /// FASTEC workcenter grouping available in the analytical layer.
    /// Mapped from DimWorkcenterExt.CostCenter.
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    public string DisplayDesignation => string.IsNullOrWhiteSpace(Designation)
        ? Code
        : Designation;

    public string DisplayText
    {
        get
        {
            var primary = DisplayDesignation;
            var codePart = string.IsNullOrWhiteSpace(Code)
                           || string.Equals(primary, Code, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" | {Code}";
            var descriptionPart = string.IsNullOrWhiteSpace(Description)
                ? string.Empty
                : $" - {Description}";

            return $"{primary}{codePart}{descriptionPart}";
        }
    }

    public override string ToString() => DisplayText;
}

public sealed class MesMachineOverviewFilter
{
    /// <summary>
    /// Backward-compatible single-workcenter filter. New UI uses WorkcenterCodes.
    /// </summary>
    public string WorkcenterCode { get; init; } = string.Empty;

    /// <summary>
    /// Selected workcenters. Empty list means no multi-select filter was supplied.
    /// </summary>
    public IReadOnlyList<string> WorkcenterCodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// FASTEC workcenter group. In the current analytical layer this is CostCenter.
    /// Empty means all groups.
    /// </summary>
    public string WorkcenterGroup { get; init; } = string.Empty;

    public int MaxRows { get; init; } = 500;
}

public sealed class MesMachineOverviewRecord
{
    public Guid WorkcenterId { get; init; }
    public string WorkcenterCode { get; init; } = string.Empty;
    public string WorkcenterDescription { get; init; } = string.Empty;
    public string WorkcenterDesignation { get; init; } = string.Empty;
    public string WorkcenterGroup { get; init; } = string.Empty;

    public Guid? ShiftId { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public DateTime? ShiftStartTime { get; init; }
    public DateTime? ShiftEndTime { get; init; }

    public Guid? OperationId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;

    public string StateName { get; init; } = string.Empty;
    public string StateCategory { get; init; } = string.Empty;
    public string StateColor { get; init; } = string.Empty;
    public string StateCategoryColor { get; init; } = string.Empty;
    public DateTime? StateStartedAt { get; init; }
    public DateTime? StateEndedAt { get; init; }
    public string StateUserText { get; init; } = string.Empty;

    public decimal? PlannedPerformancePerMinute { get; init; }
    public decimal? CurrentPerformancePerMinute { get; init; }
    public decimal? OrderTargetAmount { get; init; }
    public decimal? OrderGoodAmount { get; init; }
    public decimal? ShiftGoodAmount { get; init; }

    public TimeSpan CurrentStateDuration(DateTime now)
    {
        if (!StateStartedAt.HasValue)
        {
            return TimeSpan.Zero;
        }

        var end = StateEndedAt ?? now;
        return end > StateStartedAt.Value
            ? end - StateStartedAt.Value
            : TimeSpan.Zero;
    }
}
