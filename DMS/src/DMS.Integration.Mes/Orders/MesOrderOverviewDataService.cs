using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Orders;

/// <summary>
/// Read-only FASTEC production-order access for ORD10.
/// Header source: dbo.d_pda_po.
/// Operation source: dbo.d_pda_po_routing.
/// Actual workcenter is resolved from the analytical MDA history when available.
/// </summary>
public sealed class MesOrderOverviewDataService
{
    private readonly object _settings;
    private readonly string _analyticsSchema;
    private readonly int _commandTimeoutSeconds;

    public MesOrderOverviewDataService(object settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _analyticsSchema =
            ReadString("ReportingSchema", "Schema", "SchemaName")
            ?? "ana";

        _commandTimeoutSeconds = Math.Max(
            5,
            ReadInt(
                30,
                "CommandTimeoutSeconds",
                "SqlCommandTimeoutSeconds",
                "CommandTimeout"));
    }

    public async Task<IReadOnlyList<MesProductionOrderRecord>> GetOrdersAsync(
        MesOrderOverviewFilter filter,
        CancellationToken cancellationToken = default)
    {
        filter ??= new MesOrderOverviewFilter();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var sortExpression =
            await ResolveOrderCreatedSortExpressionAsync(
                connection,
                cancellationToken);

        // Important for both speed and memory:
        // first reduce d_pda_po to the newest requested rows, THEN aggregate
        // d_pda_po_routing only for that small order set.
        var sql = $"""
            WITH RecentOrders AS
            (
                SELECT TOP (@maxRows)
                    po.id,
                    po.code,
                    po.product_code,
                    po.product_description,
                    po.cf_customer,
                    po.status,
                    po.general_status,
                    po.production_status,
                    po.planning_status,
                    po.planning_fix_status,
                    po.archive_status,
                    po.failure_status,
                    {sortExpression} AS SortCreatedAt
                FROM dbo.d_pda_po po
                WHERE
                    NULLIF(LTRIM(RTRIM(po.code)), '') IS NOT NULL
                    AND COALESCE(po.archive_status, 0) = 0
                    AND
                    (
                        @search = ''
                        OR po.code LIKE '%' + @search + '%'
                        OR COALESCE(po.product_code, '') LIKE '%' + @search + '%'
                        OR COALESCE(po.product_description, '') LIKE '%' + @search + '%'
                        OR COALESCE(po.cf_customer, '') LIKE '%' + @search + '%'
                    )
                ORDER BY
                    {sortExpression} DESC,
                    po.change_id DESC,
                    po.code DESC
            ),
            RoutingAgg AS
            (
                SELECT
                    r.production_order_id,
                    MAX(COALESCE(r.quantity, 0)) AS TargetQuantity,
                    MAX(COALESCE(r.finished_quantity, 0)) AS FinishedQuantity,
                    MAX(COALESCE(r.scrap_quantity, 0)) AS ScrapQuantity,
                    MIN(r.planned_start_date) AS PlannedStart,
                    MAX(r.planned_end_date) AS PlannedEnd,
                    MIN(r.actual_start_date) AS ActualStart,
                    MAX(r.actual_end_date) AS ActualEnd,
                    COUNT_BIG(*) AS OperationCount
                FROM dbo.d_pda_po_routing r
                INNER JOIN RecentOrders ro
                    ON ro.id = r.production_order_id
                GROUP BY r.production_order_id
            )
            SELECT
                ro.id,
                ro.code,
                ro.product_code,
                ro.product_description,
                ro.cf_customer,
                ro.status,
                ro.general_status,
                ro.production_status,
                ro.planning_status,
                ro.planning_fix_status,
                ro.archive_status,
                ro.failure_status,
                ro.SortCreatedAt AS CreatedAt,
                COALESCE(ra.TargetQuantity, 0) AS TargetQuantity,
                COALESCE(ra.FinishedQuantity, 0) AS FinishedQuantity,
                COALESCE(ra.ScrapQuantity, 0) AS ScrapQuantity,
                ra.PlannedStart,
                ra.PlannedEnd,
                ra.ActualStart,
                ra.ActualEnd,
                COALESCE(CONVERT(int, ra.OperationCount), 0) AS OperationCount
            FROM RecentOrders ro
            LEFT JOIN RoutingAgg ra
                ON ra.production_order_id = ro.id
            ORDER BY
                ro.SortCreatedAt DESC,
                ro.code DESC;
            """;

        await using var command = CreateCommand(connection, sql);
        AddParameter(
            command,
            "@maxRows",
            Math.Clamp(filter.MaxRows, 1, 500));
        AddParameter(
            command,
            "@search",
            filter.SearchText?.Trim() ?? string.Empty);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<MesProductionOrderRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new MesProductionOrderRecord
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
                    CreatedAt = GetDateTime(reader, "CreatedAt"),
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

        return rows;
    }

    public async Task<IReadOnlyList<MesProductionOrderRecord>> SearchOrdersAsync(
        MesOrderAdvancedSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        criteria ??= new MesOrderAdvancedSearchCriteria();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var createdExpression =
            await ResolveOrderCreatedSortExpressionAsync(
                connection,
                cancellationToken);

        var headerWhere =
            new List<string>
            {
                "NULLIF(LTRIM(RTRIM(po.code)), '') IS NOT NULL"
            };

        await using var parameterOwner =
            connection.CreateCommand();

        AddLikeCondition(headerWhere, parameterOwner, "po.code", "@orderCode", criteria.OrderCode);
        AddLikeCondition(headerWhere, parameterOwner, "po.product_code", "@productCode", criteria.ProductCode);
        AddLikeCondition(headerWhere, parameterOwner, "po.product_designation", "@productDesignation", criteria.ProductDesignation);
        AddLikeCondition(headerWhere, parameterOwner, "po.product_description", "@productDescription", criteria.ProductDescription);
        AddLikeCondition(headerWhere, parameterOwner, "po.description", "@orderDescription", criteria.OrderDescription);
        AddLikeCondition(headerWhere, parameterOwner, "po.cf_customer", "@sapArticle", criteria.SapArticleNumber);
        AddLikeCondition(headerWhere, parameterOwner, "po.customer_order_code", "@customerOrderCode", criteria.CustomerOrderCode);
        AddLikeCondition(headerWhere, parameterOwner, "po.customer_code", "@customerCode", criteria.CustomerCode);

        if (!string.IsNullOrWhiteSpace(criteria.CostCenter))
        {
            headerWhere.Add(
                """
                EXISTS
                (
                    SELECT 1
                    FROM dbo.d_pda_po_routing rc
                    WHERE rc.production_order_id = po.id
                      AND COALESCE(rc.cost_center, '') LIKE @costCenter
                )
                """);

            AddParameter(
                parameterOwner,
                "@costCenter",
                $"%{criteria.CostCenter.Trim()}%");
        }

        AddEqualsCondition(headerWhere, parameterOwner, "COALESCE(po.archive_status, 0)", "@archiveStatus", criteria.ArchiveStatus);
        AddEqualsCondition(headerWhere, parameterOwner, "COALESCE(po.failure_status, 0)", "@failureStatus", criteria.FailureStatus);
        AddEqualsCondition(headerWhere, parameterOwner, "COALESCE(po.general_status, 0)", "@generalStatus", criteria.GeneralStatus);
        AddEqualsCondition(headerWhere, parameterOwner, "COALESCE(po.planning_status, 0)", "@planningStatus", criteria.PlanningStatus);
        AddEqualsCondition(headerWhere, parameterOwner, "COALESCE(po.planning_fix_status, 0)", "@planningFixStatus", criteria.PlanningFixStatus);
        AddEqualsCondition(headerWhere, parameterOwner, "COALESCE(po.production_status, 0)", "@productionStatus", criteria.ProductionStatus);

        AddDateCondition(headerWhere, parameterOwner, createdExpression, ">=", "@createdFrom", criteria.CreatedFrom);
        AddDateCondition(headerWhere, parameterOwner, createdExpression, "<", "@createdTo", criteria.CreatedToExclusive);

        var metricsWhere =
            new List<string>();

        AddDateCondition(metricsWhere, parameterOwner, "ra.PlannedStart", ">=", "@plannedStartFrom", criteria.PlannedStartFrom);
        AddDateCondition(metricsWhere, parameterOwner, "ra.PlannedStart", "<", "@plannedStartTo", criteria.PlannedStartToExclusive);
        AddDateCondition(metricsWhere, parameterOwner, "ra.PlannedEnd", ">=", "@plannedEndFrom", criteria.PlannedEndFrom);
        AddDateCondition(metricsWhere, parameterOwner, "ra.PlannedEnd", "<", "@plannedEndTo", criteria.PlannedEndToExclusive);
        AddDateCondition(metricsWhere, parameterOwner, "ra.ActualStart", ">=", "@actualStartFrom", criteria.ActualStartFrom);
        AddDateCondition(metricsWhere, parameterOwner, "ra.ActualStart", "<", "@actualStartTo", criteria.ActualStartToExclusive);
        AddDateCondition(metricsWhere, parameterOwner, "ra.ActualEnd", ">=", "@actualEndFrom", criteria.ActualEndFrom);
        AddDateCondition(metricsWhere, parameterOwner, "ra.ActualEnd", "<", "@actualEndTo", criteria.ActualEndToExclusive);

        AddDecimalCondition(metricsWhere, parameterOwner, "COALESCE(ra.TargetQuantity, 0)", ">=", "@targetMin", criteria.TargetQuantityMin);
        AddDecimalCondition(metricsWhere, parameterOwner, "COALESCE(ra.TargetQuantity, 0)", "<=", "@targetMax", criteria.TargetQuantityMax);
        AddDecimalCondition(metricsWhere, parameterOwner, "COALESCE(ra.FinishedQuantity, 0)", ">=", "@finishedMin", criteria.FinishedQuantityMin);
        AddDecimalCondition(metricsWhere, parameterOwner, "COALESCE(ra.FinishedQuantity, 0)", "<=", "@finishedMax", criteria.FinishedQuantityMax);
        AddDecimalCondition(metricsWhere, parameterOwner, "COALESCE(ra.ScrapQuantity, 0)", ">=", "@scrapMin", criteria.ScrapQuantityMin);
        AddDecimalCondition(metricsWhere, parameterOwner, "COALESCE(ra.ScrapQuantity, 0)", "<=", "@scrapMax", criteria.ScrapQuantityMax);

        const string progressExpression =
            """
            CASE
                WHEN COALESCE(ra.TargetQuantity, 0) > 0
                    THEN COALESCE(ra.FinishedQuantity, 0) * 100.0 / ra.TargetQuantity
                ELSE 0
            END
            """;

        AddDecimalCondition(metricsWhere, parameterOwner, progressExpression, ">=", "@progressMin", criteria.ProgressPercentMin);
        AddDecimalCondition(metricsWhere, parameterOwner, progressExpression, "<=", "@progressMax", criteria.ProgressPercentMax);

        var headerSql =
            string.Join(
                "\n                    AND ",
                headerWhere);

        var metricsSql =
            metricsWhere.Count == 0
                ? "1 = 1"
                : string.Join(
                    "\n                    AND ",
                    metricsWhere);

        var sql =
            $"""
            WITH HeaderCandidates AS
            (
                SELECT
                    po.id,
                    po.code,
                    po.product_code,
                    po.product_designation,
                    po.product_description,
                    po.description,
                    po.customer_order_code,
                    po.customer_code,
                    po.cf_customer,
                    po.status,
                    po.general_status,
                    po.production_status,
                    po.planning_status,
                    po.planning_fix_status,
                    po.archive_status,
                    po.failure_status,
                    po.change_id,
                    {createdExpression} AS CreatedAt
                FROM dbo.d_pda_po po
                WHERE
                    {headerSql}
            ),
            RoutingAgg AS
            (
                SELECT
                    r.production_order_id,
                    MAX(COALESCE(r.quantity, 0)) AS TargetQuantity,
                    MAX(COALESCE(r.finished_quantity, 0)) AS FinishedQuantity,
                    MAX(COALESCE(r.scrap_quantity, 0)) AS ScrapQuantity,
                    MIN(r.planned_start_date) AS PlannedStart,
                    MAX(r.planned_end_date) AS PlannedEnd,
                    MIN(r.actual_start_date) AS ActualStart,
                    MAX(r.actual_end_date) AS ActualEnd,
                    COUNT_BIG(*) AS OperationCount
                FROM dbo.d_pda_po_routing r
                INNER JOIN HeaderCandidates hc
                    ON hc.id = r.production_order_id
                GROUP BY r.production_order_id
            ),
            Filtered AS
            (
                SELECT TOP (@maxRows)
                    hc.id,
                    hc.code,
                    hc.product_code,
                    hc.product_description,
                    hc.cf_customer,
                    hc.status,
                    hc.general_status,
                    hc.production_status,
                    hc.planning_status,
                    hc.planning_fix_status,
                    hc.archive_status,
                    hc.failure_status,
                    hc.CreatedAt,
                    COALESCE(ra.TargetQuantity, 0) AS TargetQuantity,
                    COALESCE(ra.FinishedQuantity, 0) AS FinishedQuantity,
                    COALESCE(ra.ScrapQuantity, 0) AS ScrapQuantity,
                    ra.PlannedStart,
                    ra.PlannedEnd,
                    ra.ActualStart,
                    ra.ActualEnd,
                    COALESCE(CONVERT(int, ra.OperationCount), 0) AS OperationCount,
                    hc.change_id
                FROM HeaderCandidates hc
                LEFT JOIN RoutingAgg ra
                    ON ra.production_order_id = hc.id
                WHERE
                    {metricsSql}
                ORDER BY
                    hc.CreatedAt DESC,
                    hc.change_id DESC,
                    hc.code DESC
            )
            SELECT
                id,
                code,
                product_code,
                product_description,
                cf_customer,
                status,
                general_status,
                production_status,
                planning_status,
                planning_fix_status,
                archive_status,
                failure_status,
                CreatedAt,
                TargetQuantity,
                FinishedQuantity,
                ScrapQuantity,
                PlannedStart,
                PlannedEnd,
                ActualStart,
                ActualEnd,
                OperationCount
            FROM Filtered
            ORDER BY
                CreatedAt DESC,
                change_id DESC,
                code DESC;
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        AddParameter(
            command,
            "@maxRows",
            Math.Clamp(
                criteria.MaxRows,
                1,
                10000));

        foreach (DbParameter parameter
                 in parameterOwner.Parameters)
        {
            var copy =
                command.CreateParameter();

            copy.ParameterName =
                parameter.ParameterName;

            copy.DbType =
                parameter.DbType;

            copy.Value =
                parameter.Value;

            command.Parameters.Add(
                copy);
        }

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var rows =
            new List<MesProductionOrderRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new MesProductionOrderRecord
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
                    CreatedAt = GetDateTime(reader, "CreatedAt"),
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

        return rows;
    }

    private static void AddLikeCondition(
        ICollection<string> where,
        DbCommand command,
        string expression,
        string parameterName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        where.Add(
            $"COALESCE({expression}, '') LIKE {parameterName}");

        AddParameter(
            command,
            parameterName,
            $"%{value.Trim()}%");
    }

    private static void AddEqualsCondition(
        ICollection<string> where,
        DbCommand command,
        string expression,
        string parameterName,
        int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        where.Add(
            $"{expression} = {parameterName}");

        AddParameter(
            command,
            parameterName,
            value.Value);
    }

    private static void AddDateCondition(
        ICollection<string> where,
        DbCommand command,
        string expression,
        string comparison,
        string parameterName,
        DateTime? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        where.Add(
            $"{expression} {comparison} {parameterName}");

        var parameter =
            command.CreateParameter();

        parameter.ParameterName =
            parameterName;

        parameter.DbType =
            DbType.DateTime2;

        parameter.Value =
            value.Value;

        command.Parameters.Add(
            parameter);
    }

    private static void AddDecimalCondition(
        ICollection<string> where,
        DbCommand command,
        string expression,
        string comparison,
        string parameterName,
        decimal? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        where.Add(
            $"{expression} {comparison} {parameterName}");

        var parameter =
            command.CreateParameter();

        parameter.ParameterName =
            parameterName;

        parameter.DbType =
            DbType.Decimal;

        parameter.Value =
            value.Value;

        command.Parameters.Add(
            parameter);
    }

    public async Task<IReadOnlyList<MesProductionOrderOperationRecord>> GetOperationsAsync(
        Guid productionOrderId,
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId == Guid.Empty)
        {
            return Array.Empty<MesProductionOrderOperationRecord>();
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var resourceDisplayColumn =
            await ResolveResourceDisplayColumnAsync(
                connection,
                cancellationToken);

        // Column names are never accepted from user input. The helper returns
        // only one member of a hard-coded whitelist verified against
        // INFORMATION_SCHEMA.
        var resourceExpression =
            resourceDisplayColumn is null
                ? "NULL"
                : $"res.[{resourceDisplayColumn}]";

        var sql = $"""
            SELECT
                r.id,
                r.code,
                r.description,
                r.status,
                r.general_status,
                r.production_status,
                r.planning_status,
                r.planning_fix_status,
                r.archive_status,
                r.failure_status,
                COALESCE(r.quantity, 0) AS TargetQuantity,
                COALESCE(r.finished_quantity, 0) AS FinishedQuantity,
                COALESCE(r.scrap_quantity, 0) AS ScrapQuantity,
                r.planned_start_date,
                r.planned_end_date,
                r.actual_start_date,
                r.actual_end_date,
                COALESCE(
                    NULLIF(planned.Workcenters, ''),
                    NULLIF(actualWc.WorkcenterCode, ''),
                    NULLIF(LTRIM(RTRIM(r.cost_center)), ''),
                    '') AS WorkcenterCode
            FROM dbo.d_pda_po_routing r
            OUTER APPLY
            (
                SELECT
                    STRING_AGG(x.DisplayCode, ', ') AS Workcenters
                FROM
                (
                    SELECT DISTINCT
                        COALESCE(
                            NULLIF(
                                LTRIM(
                                    RTRIM(
                                        CONVERT(
                                            nvarchar(200),
                                            {resourceExpression}))),
                                ''),
                            NULLIF(
                                LTRIM(
                                    RTRIM(
                                        CONVERT(
                                            nvarchar(200),
                                            grp.code))),
                                '')) AS DisplayCode
                    FROM dbo.d_dp_routing planning
                    INNER JOIN dbo.d_dp_routing_pres_link link
                        ON link.planning_routing_id = planning.id
                    LEFT JOIN dbo.m_res_resource res
                        ON res.id = link.resource_id
                    LEFT JOIN dbo.m_res_group grp
                        ON grp.id = link.group_id
                    WHERE planning.operation_id = r.id
                ) x
                WHERE x.DisplayCode IS NOT NULL
            ) planned
            OUTER APPLY
            (
                SELECT TOP (1)
                    w.Code AS WorkcenterCode
                FROM [{_analyticsSchema}].[FactMdaMes] m
                INNER JOIN [{_analyticsSchema}].[DimMdaOperation] o
                    ON o.ID = m.OperationID
                INNER JOIN [{_analyticsSchema}].[DimWorkcenter] w
                    ON w.ID = m.WorkcenterID
                WHERE o.OrderCode = @orderCode
                  AND o.OperationCode = r.code
                ORDER BY
                    m.Starttime DESC,
                    m.ID DESC
            ) actualWc
            WHERE r.production_order_id = @productionOrderId
            ORDER BY
                TRY_CONVERT(int, r.code),
                r.code;
            """;

        await using var command = CreateCommand(connection, sql);
        AddGuidParameter(command, "@productionOrderId", productionOrderId);
        AddParameter(command, "@orderCode", orderCode ?? string.Empty);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var rows =
            new List<MesProductionOrderOperationRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new MesProductionOrderOperationRecord
                {
                    Id = GetGuid(reader, "id") ?? Guid.Empty,
                    OperationCode = GetString(reader, "code"),
                    Description = GetString(reader, "description"),
                    WorkcenterCode = GetString(reader, "WorkcenterCode"),
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
                    PlannedStart = GetDateTime(reader, "planned_start_date"),
                    PlannedEnd = GetDateTime(reader, "planned_end_date"),
                    ActualStart = GetDateTime(reader, "actual_start_date"),
                    ActualEnd = GetDateTime(reader, "actual_end_date")
                });
        }

        return rows;
    }

    private async Task<string?> ResolveResourceDisplayColumnAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'm_res_resource';
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var existing =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                existing.Add(
                    reader.GetString(0));
            }
        }

        // FASTEC operator-facing workcenter/resource identifier.
        foreach (var candidate in new[]
                 {
                     "code",
                     "designation",
                     "description"
                 })
        {
            if (existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<string> ResolveOrderCreatedSortExpressionAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        // The Hranice FASTEC build exposes a "Created at" value in the order UI,
        // but the exact dbo.d_pda_po column name may differ by FASTEC version.
        // Only this hard-coded whitelist can ever become part of SQL text.
        var candidates =
            new[]
            {
                "created_at",
                "created_date",
                "creation_date",
                "creation_datetime",
                "inserted_at",
                "insert_date",
                "changed_at",
                "change_date"
            };

        const string sql = """
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'd_pda_po';
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var existing =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                existing.Add(
                    reader.GetString(0));
            }
        }

        foreach (var candidate in candidates)
        {
            if (existing.Contains(candidate))
            {
                return $"po.[{candidate}]";
            }
        }

        // Safe fallback when this FASTEC release has no explicit creation
        // timestamp on d_pda_po. change_id is monotonic enough for "latest"
        // ordering and avoids loading thousands of old rows.
        return "CONVERT(bigint, po.change_id)";
    }

    private DbConnection CreateConnection()
    {
        var directConnectionString =
            ReadConnectionString();

        if (!string.IsNullOrWhiteSpace(
                directConnectionString))
        {
            return CreateSqlServerConnection(
                directConnectionString);
        }

        var server =
            ReadString(
                "Server",
                "SqlServer",
                "ServerName",
                "DataSource",
                "Host",
                "Address",
                "ServerAddress");

        var database =
            ReadString(
                "Database",
                "DatabaseName",
                "InitialCatalog",
                "Catalog");

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException(
                "MESSET does not contain a SQL server address.");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "MESSET does not contain a SQL database name.");
        }

        var encrypt =
            ReadBool(
                true,
                "Encrypt",
                "UseEncryption");

        var trust =
            ReadBool(
                true,
                "TrustServerCertificate",
                "TrustCertificate");

        var timeout =
            Math.Max(
                2,
                ReadInt(
                    8,
                    "ConnectionTimeoutSeconds",
                    "ConnectTimeoutSeconds",
                    "ConnectionTimeout"));

        var connectionString =
            $"Server={server};Database={database};Integrated Security=True;Encrypt={encrypt};TrustServerCertificate={trust};Connect Timeout={timeout};Application Name=DMS ORD10;";

        return CreateSqlServerConnection(
            connectionString);
    }

    private string? ReadConnectionString()
    {
        var propertyValue =
            ReadString(
                "ConnectionString",
                "SqlConnectionString");

        if (!string.IsNullOrWhiteSpace(
                propertyValue))
        {
            return propertyValue;
        }

        foreach (var methodName in new[]
                 {
                     "BuildConnectionString",
                     "CreateConnectionString",
                     "GetConnectionString"
                 })
        {
            var method =
                _settings
                    .GetType()
                    .GetMethod(
                        methodName,
                        BindingFlags.Public
                        | BindingFlags.Instance
                        | BindingFlags.IgnoreCase,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);

            if (method?.ReturnType == typeof(string)
                && method.Invoke(
                       _settings,
                       null) is string value
                && !string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static DbConnection CreateSqlServerConnection(
        string connectionString)
    {
        var type =
            Type.GetType(
                "Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient",
                throwOnError: false)
            ?? Type.GetType(
                "System.Data.SqlClient.SqlConnection, System.Data.SqlClient",
                throwOnError: false);

        if (type is null)
        {
            throw new InvalidOperationException(
                "No SQL Server provider is available.");
        }

        return
            (DbConnection)(
                Activator.CreateInstance(
                    type,
                    connectionString)
                ?? throw new InvalidOperationException(
                    "Could not create SQL Server connection."));
    }

    private DbCommand CreateCommand(
        DbConnection connection,
        string sql)
    {
        var command =
            connection.CreateCommand();

        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout =
            _commandTimeoutSeconds;

        return command;
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName = name;
        parameter.Value =
            value ?? DBNull.Value;

        command.Parameters.Add(
            parameter);
    }

    private static void AddGuidParameter(
        DbCommand command,
        string name,
        Guid value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName = name;
        parameter.DbType = DbType.Guid;
        parameter.Value = value;

        command.Parameters.Add(
            parameter);
    }

    private string? ReadString(
        params string[] names)
    {
        foreach (var name in names)
        {
            var property =
                _settings
                    .GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Public
                        | BindingFlags.Instance
                        | BindingFlags.IgnoreCase);

            if (property?.GetValue(
                    _settings) is string value
                && !string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private int ReadInt(
        int fallback,
        params string[] names)
    {
        foreach (var name in names)
        {
            var property =
                _settings
                    .GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Public
                        | BindingFlags.Instance
                        | BindingFlags.IgnoreCase);

            var value =
                property?.GetValue(
                    _settings);

            if (value is null)
            {
                continue;
            }

            try
            {
                return Convert.ToInt32(
                    value,
                    CultureInfo.InvariantCulture);
            }
            catch
            {
                // Ignore invalid optional setting.
            }
        }

        return fallback;
    }

    private bool ReadBool(
        bool fallback,
        params string[] names)
    {
        foreach (var name in names)
        {
            var property =
                _settings
                    .GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Public
                        | BindingFlags.Instance
                        | BindingFlags.IgnoreCase);

            var value =
                property?.GetValue(
                    _settings);

            if (value is bool boolean)
            {
                return boolean;
            }

            if (value is string text
                && bool.TryParse(
                    text,
                    out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static string GetString(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(ordinal),
                  CultureInfo.CurrentCulture)
              ?? string.Empty;
    }

    private static int GetInt(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        return Convert.ToInt32(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static decimal GetDecimal(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        if (reader.IsDBNull(ordinal))
        {
            return 0m;
        }

        return Convert.ToDecimal(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static Guid? GetGuid(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value =
            reader.GetValue(ordinal);

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(
                text,
                out var guid) => guid,
            _ => null
        };
    }

    private static DateTime? GetDateTime(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value =
            reader.GetValue(ordinal);

        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset offset => offset.DateTime,
            _ when DateTime.TryParse(
                Convert.ToString(
                    value,
                    CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed) => parsed,
            _ => null
        };
    }
}
