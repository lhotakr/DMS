using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Orders;

/// <summary>Read-only exact-code MES lookup used by QA; includes archived orders.</summary>
public sealed partial class MesOrderOverviewDataService
{
    public async Task<IReadOnlyList<MesProductionOrderRecord>> GetOrdersByCodesAsync(
        IEnumerable<string> orderCodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderCodes);
        var codes = orderCodes.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length == 0) return Array.Empty<MesProductionOrderRecord>();

        var records = new List<MesProductionOrderRecord>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // Keep well below SQL Server's 2100-parameter limit.
        const int batchSize = 150;
        for (var offset = 0; offset < codes.Length; offset += batchSize)
        {
            var batch = codes.Skip(offset).Take(batchSize).ToArray();
            var names = batch.Select((_, i) => "@order" + i).ToArray();
            var inList = string.Join(",", names);
            var sql = $"""
                WITH RequestedOrders AS
                (
                    SELECT po.id, po.code, po.product_code, po.product_description,
                        po.cf_customer, po.status, po.general_status,
                        po.production_status, po.planning_status,
                        po.planning_fix_status, po.archive_status,
                        po.failure_status, po.change_id
                    FROM dbo.d_pda_po po
                    WHERE po.code IN ({inList})
                ),
                Routing AS
                (
                    SELECT r.production_order_id,
                        MAX(COALESCE(r.quantity,0)) AS TargetQuantity,
                        MAX(COALESCE(r.finished_quantity,0)) AS FinishedQuantity,
                        MAX(COALESCE(r.scrap_quantity,0)) AS ScrapQuantity,
                        MIN(r.planned_start_date) AS PlannedStart,
                        MAX(r.planned_end_date) AS PlannedEnd,
                        MIN(r.actual_start_date) AS ActualStart,
                        MAX(r.actual_end_date) AS ActualEnd,
                        COUNT_BIG(*) AS OperationCount
                    FROM dbo.d_pda_po_routing r
                    INNER JOIN RequestedOrders po ON po.id = r.production_order_id
                    GROUP BY r.production_order_id
                )
                SELECT po.id, po.code, po.product_code, po.product_description,
                    po.cf_customer, po.status, po.general_status,
                    po.production_status, po.planning_status, po.planning_fix_status,
                    po.archive_status, po.failure_status,
                    COALESCE(r.TargetQuantity,0) AS TargetQuantity,
                    COALESCE(r.FinishedQuantity,0) AS FinishedQuantity,
                    COALESCE(r.ScrapQuantity,0) AS ScrapQuantity,
                    r.PlannedStart, r.PlannedEnd, r.ActualStart, r.ActualEnd,
                    COALESCE(CONVERT(int,r.OperationCount),0) AS OperationCount
                FROM RequestedOrders po
                LEFT JOIN Routing r ON r.production_order_id = po.id
                ORDER BY po.change_id DESC;
                """;
            await using var command = CreateCommand(connection, sql);
            for (var i = 0; i < batch.Length; i++) AddParameter(command, names[i], batch[i]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(new MesProductionOrderRecord
                {
                    Id = GetGuid(reader, "id") ?? Guid.Empty,
                    OrderCode = GetString(reader, "code"),
                    ProductCode = GetString(reader, "product_code"),
                    ProductDescription = GetString(reader, "product_description"),
                    SapArticleNumber = GetString(reader, "cf_customer"),
                    RawStatus = GetInt(reader, "status"),
                    GeneralStatus = GetInt(reader, "general_status"),
                    ProductionStatus = GetInt(reader, "production_status"),
                    PlanningStatus = GetInt(reader, "planning_status"),
                    PlanningFixStatus = GetInt(reader, "planning_fix_status"),
                    ArchiveStatus = GetInt(reader, "archive_status"),
                    FailureStatus = GetInt(reader, "failure_status"),
                    TargetQuantity = GetDecimal(reader, "TargetQuantity"),
                    FinishedQuantity = GetDecimal(reader, "FinishedQuantity"),
                    ScrapQuantity = GetDecimal(reader, "ScrapQuantity"),
                    PlannedStart = GetDateTime(reader, "PlannedStart"),
                    PlannedEnd = GetDateTime(reader, "PlannedEnd"),
                    ActualStart = GetDateTime(reader, "ActualStart"),
                    ActualEnd = GetDateTime(reader, "ActualEnd"),
                    OperationCount = GetInt(reader, "OperationCount")
                });
            }
        }
        return records.GroupBy(x => x.OrderCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First()).ToList();
    }
}
