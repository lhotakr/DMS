using System;
using System.Collections.Generic;

namespace DMS.Integration.Mes.Orders;

public sealed class MesOrderOverviewFilter
{
    public string SearchText { get; init; } = string.Empty;
    public int MaxRows { get; init; } = 500;
}

public sealed class MesProductionOrderRecord
{
    public Guid Id { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;

    /// <summary>
    /// FASTEC dbo.d_pda_po.cf_customer.
    /// In the Hranice database this contains the 10-digit SAP material/article number.
    /// </summary>
    public string SapArticleNumber { get; init; } = string.Empty;

    public int RawStatus { get; init; }
    public int GeneralStatus { get; init; }
    public int ProductionStatus { get; init; }
    public int PlanningStatus { get; init; }
    public int PlanningFixStatus { get; init; }
    public int ArchiveStatus { get; init; }
    public int FailureStatus { get; init; }

    public decimal TargetQuantity { get; init; }
    public decimal FinishedQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }

    public DateTime? CreatedAt { get; init; }
    public DateTime? PlannedStart { get; init; }
    public DateTime? PlannedEnd { get; init; }
    public DateTime? ActualStart { get; init; }
    public DateTime? ActualEnd { get; init; }

    public int OperationCount { get; init; }

    /// <summary>
    /// FASTEC "Status" filter is the general_status axis:
    /// 0 CRTD, 1 REL, 2 PREL, 3 RWDN, 4 CCLD, 5 UCPL, 6 CLSD.
    /// </summary>
    public string GeneralStatusCode =>
        MesProductionOrderStatusFormatter.FormatGeneralStatus(
            GeneralStatus);

    /// <summary>
    /// User-facing row status. Production is a separate FASTEC status axis,
    /// so a released order in production is displayed as REL PROD,
    /// while filtering still uses GeneralStatusCode=REL.
    /// </summary>
    public string StatusCode =>
        GeneralStatusCode == "REL"
        && ProductionStatus == 1
            ? "REL PROD"
            : GeneralStatusCode;

    public string StatusIcon =>
        MesProductionOrderStatusFormatter.Icon(StatusCode);

    public double ProgressPercent =>
        TargetQuantity > 0m
            ? Math.Clamp((double)(FinishedQuantity / TargetQuantity * 100m), 0d, 100d)
            : 0d;
}

public sealed class MesProductionOrderOperationRecord
{
    public Guid Id { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string WorkcenterCode { get; init; } = string.Empty;

    public int RawStatus { get; init; }
    public int GeneralStatus { get; init; }
    public int ProductionStatus { get; init; }
    public int PlanningStatus { get; init; }
    public int PlanningFixStatus { get; init; }
    public int ArchiveStatus { get; init; }
    public int FailureStatus { get; init; }

    public decimal TargetQuantity { get; init; }
    public decimal FinishedQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }

    public DateTime? PlannedStart { get; init; }
    public DateTime? PlannedEnd { get; init; }
    public DateTime? ActualStart { get; init; }
    public DateTime? ActualEnd { get; init; }

    /// <summary>
    /// FASTEC "Status" filter is the general_status axis:
    /// 0 CRTD, 1 REL, 2 PREL, 3 RWDN, 4 CCLD, 5 UCPL, 6 CLSD.
    /// </summary>
    public string GeneralStatusCode =>
        MesProductionOrderStatusFormatter.FormatGeneralStatus(
            GeneralStatus);

    /// <summary>
    /// User-facing row status. Production is a separate FASTEC status axis,
    /// so a released order in production is displayed as REL PROD,
    /// while filtering still uses GeneralStatusCode=REL.
    /// </summary>
    public string StatusCode =>
        GeneralStatusCode == "REL"
        && ProductionStatus == 1
            ? "REL PROD"
            : GeneralStatusCode;

    public string StatusIcon =>
        MesProductionOrderStatusFormatter.Icon(StatusCode);
}

public static class MesProductionOrderStatusFormatter
{
    public static readonly IReadOnlyList<string> MesGeneralStatuses =
        new[]
        {
            "CRTD",
            "REL",
            "PREL",
            "RWDN",
            "CCLD",
            "UCPL",
            "CLSD"
        };

    public static string FormatGeneralStatus(
        int generalStatus)
    {
        return generalStatus switch
        {
            0 => "CRTD",
            1 => "REL",
            2 => "PREL",
            3 => "RWDN",
            4 => "CCLD",
            5 => "UCPL",
            6 => "CLSD",
            _ => $"G{generalStatus}"
        };
    }

    public static string Icon(string statusCode)
    {
        return statusCode switch
        {
            "REL PROD" => "⚙",
            "REL" => "●",
            "PREL" => "◐",
            "RWDN" => "↩",
            "CCLD" => "✕",
            "UCPL" => "◒",
            "CLSD" => "✓",
            "CRTD" => "○",
            _ => "?"
        };
    }
}
