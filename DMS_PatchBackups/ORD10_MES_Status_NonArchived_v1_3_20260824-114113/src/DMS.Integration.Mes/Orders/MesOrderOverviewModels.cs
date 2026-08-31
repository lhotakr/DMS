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

    public DateTime? PlannedStart { get; init; }
    public DateTime? PlannedEnd { get; init; }
    public DateTime? ActualStart { get; init; }
    public DateTime? ActualEnd { get; init; }

    public int OperationCount { get; init; }

    public string StatusCode =>
        MesProductionOrderStatusFormatter.Format(
            GeneralStatus,
            ProductionStatus,
            ArchiveStatus,
            FailureStatus);

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

    public string StatusCode =>
        MesProductionOrderStatusFormatter.Format(
            GeneralStatus,
            ProductionStatus,
            ArchiveStatus,
            FailureStatus);

    public string StatusIcon =>
        MesProductionOrderStatusFormatter.Icon(StatusCode);
}

public static class MesProductionOrderStatusFormatter
{
    public static string Format(
        int generalStatus,
        int productionStatus,
        int archiveStatus,
        int failureStatus)
    {
        if (failureStatus != 0)
        {
            return "ERROR";
        }

        // Verified against FastecCZE:
        // general=1 + production=0 -> REL
        // general=1 + production=1 -> REL PROD
        if (generalStatus == 1 && productionStatus == 1)
        {
            return "REL PROD";
        }

        if (generalStatus == 1)
        {
            return "REL";
        }

        // FASTEC closed/archived orders observed in the production-order overview.
        // Keep this intentionally conservative; unknown combinations stay visible as raw G/P state.
        if (archiveStatus == 1 || generalStatus == 4)
        {
            return "CLSD";
        }

        return $"G{generalStatus}/P{productionStatus}";
    }

    public static string Icon(string statusCode)
    {
        return statusCode switch
        {
            "REL PROD" => "⚙",
            "REL" => "●",
            "CLSD" => "✓",
            "ERROR" => "⚠",
            _ => "○"
        };
    }
}
