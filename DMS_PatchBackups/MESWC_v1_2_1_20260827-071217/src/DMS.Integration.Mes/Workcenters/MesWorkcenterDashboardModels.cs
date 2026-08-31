using DMS.Integration.Mes.Live;
using DMS.Integration.Mes.Reporting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DMS.Integration.Mes.Workcenters;

public sealed class MesWorkcenterDashboardSnapshot
{
    public string WorkcenterCode { get; init; } = string.Empty;
    public string WorkcenterDescription { get; init; } = string.Empty;
    public DateTime LoadedAt { get; init; }

    public MesMachineOverviewRecord? Live { get; init; }
    public MesWorkcenterCurrentContext? CurrentContext { get; init; }
    public MesWorkcenterOrderRecord? ActiveOrder { get; init; }
    public Mes06OeeReportRecord? Oee { get; init; }

    public IReadOnlyList<MesWorkcenterOrderRecord> AssignedOrders { get; init; } = Array.Empty<MesWorkcenterOrderRecord>();
    public IReadOnlyList<MesWorkcenterOperatorRecord> Operators { get; init; } = Array.Empty<MesWorkcenterOperatorRecord>();
    public IReadOnlyList<MesWorkcenterDowntimeRecord> StateSummary { get; init; } = Array.Empty<MesWorkcenterDowntimeRecord>();
    public MesWorkcenterOrderCounterSummary OrderCounters { get; init; } = new();
    public IReadOnlyList<Mes06ProductionGraphRecord> GraphRows { get; init; } = Array.Empty<Mes06ProductionGraphRecord>();
    public IReadOnlyList<MesReportingStateColor> StateColors { get; init; } = Array.Empty<MesReportingStateColor>();

    public DateTime? ShiftStart => Live?.ShiftStartTime ?? CurrentContext?.ShiftStarttime;
    public DateTime? ShiftEnd => Live?.ShiftEndTime ?? CurrentContext?.ShiftEndtime;

    public string ShiftName =>
        !string.IsNullOrWhiteSpace(Live?.ShiftName)
            ? Live!.ShiftName
            : CurrentContext?.ShiftName ?? string.Empty;

    public decimal? PlannedPerformance =>
        Live?.PlannedPerformancePerMinute
        ?? ActiveOrder?.PlannedPerformance;

    public decimal? ShiftNorm => ActiveOrder?.ShiftNorm;

    public decimal? ExpectedByNow
    {
        get
        {
            if (!ShiftNorm.HasValue
                || !ShiftStart.HasValue
                || !ShiftEnd.HasValue
                || ShiftEnd.Value <= ShiftStart.Value)
            {
                return null;
            }

            var now = LoadedAt < ShiftEnd.Value ? LoadedAt : ShiftEnd.Value;
            if (now <= ShiftStart.Value)
            {
                return 0m;
            }

            var ratio = (decimal)((now - ShiftStart.Value).TotalSeconds /
                                  (ShiftEnd.Value - ShiftStart.Value).TotalSeconds);
            ratio = Math.Clamp(ratio, 0m, 1m);
            return ShiftNorm.Value * ratio;
        }
    }
}

public sealed class MesWorkcenterCurrentContext
{
    public Guid MesId { get; init; }
    public Guid? OperationId { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public string OrderCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;
    public string ShiftName { get; init; } = string.Empty;
    public DateTime? ShiftStarttime { get; init; }
    public DateTime? ShiftEndtime { get; init; }
    public DateTime? MesStarttime { get; init; }
}

public sealed class MesWorkcenterOrderRecord
{
    private static readonly Regex FirstNumberRegex = new(
        @"(?<!\d)(\d+(?:[\.,]\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Guid ProductionOrderId { get; init; }
    public Guid OperationId { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;
    public string SapArticleNumber { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string RoutingDescription { get; init; } = string.Empty;
    public int GeneralStatus { get; init; }
    public int ProductionStatus { get; init; }
    public decimal TargetQuantity { get; init; }
    public decimal FinishedQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }
    public decimal? PlannedPerformance { get; init; }
    public DateTime? PlannedStart { get; init; }
    public DateTime? PlannedEnd { get; init; }
    public DateTime? ActualStart { get; init; }
    public DateTime? ActualEnd { get; init; }
    public bool IsActive { get; set; }

    public decimal RemainingQuantity => Math.Max(0m, TargetQuantity - FinishedQuantity);

    public double ProgressPercent => TargetQuantity > 0m
        ? Math.Clamp((double)(FinishedQuantity / TargetQuantity * 100m), 0d, 100d)
        : 0d;

    public decimal? ShiftNorm
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RoutingDescription))
            {
                return null;
            }

            var match = FirstNumberRegex.Match(RoutingDescription);
            if (!match.Success)
            {
                return null;
            }

            var raw = match.Groups[1].Value.Replace(',', '.');
            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
    }

    public string StatusCode
    {
        get
        {
            var general = GeneralStatus switch
            {
                0 => "CRTD",
                1 => "REL",
                2 => "PREL",
                3 => "RWDN",
                4 => "CCLD",
                5 => "UCPL",
                6 => "CLSD",
                _ => $"G{GeneralStatus}"
            };

            return general == "REL" && ProductionStatus == 1 ? "REL PROD" : general;
        }
    }

    public string TargetText => TargetQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string FinishedText => FinishedQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string RemainingText => RemainingQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string ScrapText => ScrapQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string ProgressText => $"{ProgressPercent:0}%";
}


public sealed class MesWorkcenterOrderCounterSummary
{
    public decimal ScrapProduction { get; init; }
    public decimal ScrapGlass { get; init; }
    public decimal DevelopmentDepartment { get; init; }
    public decimal QualityDepartment { get; init; }
    public decimal Setup { get; init; }
    public decimal WashedBottles { get; init; }
    public decimal TransportLogistics { get; init; }
}

public sealed class MesWorkcenterOperatorRecord
{
    public string HumanCode { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public DateTime? LoginTime { get; init; }
    public string ShiftName { get; init; } = string.Empty;

    public string Personnel
    {
        get
        {
            var name = !string.IsNullOrWhiteSpace(LastName) && !string.IsNullOrWhiteSpace(FirstName)
                ? $"{LastName}, {FirstName}"
                : !string.IsNullOrWhiteSpace(LastName) ? LastName : FirstName;

            if (string.IsNullOrWhiteSpace(HumanCode))
            {
                return name;
            }

            return string.IsNullOrWhiteSpace(name) ? $"[{HumanCode}]" : $"{name} [{HumanCode}]";
        }
    }

    public string LoginTimeText => LoginTime.HasValue
        ? LoginTime.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
        : string.Empty;
}

public sealed class MesWorkcenterDowntimeRecord
{
    public string StateName { get; init; } = string.Empty;
    public int Occurrences { get; init; }
    public double DurationSeconds { get; init; }
    public string Color { get; init; } = string.Empty;

    public string DurationText
    {
        get
        {
            var value = TimeSpan.FromSeconds(Math.Max(0d, DurationSeconds));
            var hours = (int)value.TotalHours;
            return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }
    }
}
