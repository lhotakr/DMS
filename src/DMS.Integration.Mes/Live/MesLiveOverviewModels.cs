using System;
using System.Collections.Generic;

namespace DMS.Integration.Mes.Live;

public sealed class MesShiftRecord
{
    public Guid Id { get; init; }
    public int DateId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public string Name { get; init; } = string.Empty;

    public string DisplayText
    {
        get
        {
            if (Id == Guid.Empty)
            {
                return string.IsNullOrWhiteSpace(Name) ? "Všechny směny" : Name;
            }

            var name = string.IsNullOrWhiteSpace(Name) ? "Směna" : Name;
            return $"{name} ({StartTime:dd.MM. HH:mm}-{EndTime:HH:mm})";
        }
    }

    public bool IsCurrent(DateTime now) => now >= StartTime && now < EndTime;

    public override string ToString() => DisplayText;
}

public sealed class MesLiveWorkcenterRecord
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(Description)
        ? Code
        : $"{Code} - {Description}";

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

    public Guid? ShiftId { get; init; }
    public int MaxRows { get; init; } = 500;
}

public sealed class MesMachineOverviewRecord
{
    public Guid WorkcenterId { get; init; }
    public string WorkcenterCode { get; init; } = string.Empty;
    public string WorkcenterDescription { get; init; } = string.Empty;

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
