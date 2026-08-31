using DMS.Integration.Mes.Live;
using DMS.Integration.Mes.Reporting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Workcenters;

/// <summary>
/// Read-only orchestration for MESWC. Reuses established MES live / MES06 layers
/// and keeps workstation-specific SQL outside of DMS.Desktop.
/// </summary>
public sealed class MesWorkcenterDashboardService
{
    private readonly object _settings;
    private readonly string _analyticsSchema;
    private readonly int _commandTimeoutSeconds;
    private readonly MesLiveOverviewDataService _liveService;
    private readonly MesReportingEnrichmentService _reportingService;
    private IReadOnlyList<MesReportingStateColor>? _stateColors;

    public MesWorkcenterDashboardService(object settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _analyticsSchema = ReadString("ReportingSchema", "Schema", "SchemaName") ?? "ana";
        _commandTimeoutSeconds = Math.Max(5, ReadInt(30, "CommandTimeoutSeconds", "SqlCommandTimeoutSeconds", "CommandTimeout"));
        _liveService = new MesLiveOverviewDataService(settings);
        _reportingService = new MesReportingEnrichmentService(settings);
    }

    public async Task<MesWorkcenterDashboardSnapshot> GetDashboardAsync(
        string workcenterCode,
        CancellationToken cancellationToken = default)
    {
        var code = workcenterCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("MESWC requires a work-center code.", nameof(workcenterCode));
        }

        var liveRows = await _liveService.GetOverviewAsync(
            new MesMachineOverviewFilter
            {
                WorkcenterCodes = new[] { code },
                MaxRows = 5
            },
            cancellationToken);

        var live = liveRows.FirstOrDefault();
        var context = await LoadCurrentContextAsync(code, cancellationToken);

        var activeOrderCode = context?.OrderCode ?? live?.OrderCode ?? string.Empty;
        var activeOperationCode = context?.OperationCode ?? string.Empty;

        var assignedOrders = await LoadAssignedOrdersAsync(
            code,
            activeOrderCode,
            activeOperationCode,
            cancellationToken);

        var activeOrder = assignedOrders.FirstOrDefault(row =>
            string.Equals(row.OrderCode, activeOrderCode, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(activeOperationCode)
                || string.Equals(row.OperationCode, activeOperationCode, StringComparison.OrdinalIgnoreCase)));

        if (activeOrder is null && !string.IsNullOrWhiteSpace(activeOrderCode))
        {
            activeOrder = await LoadOrderOperationAsync(activeOrderCode, activeOperationCode, cancellationToken);
            if (activeOrder is not null)
            {
                activeOrder.IsActive = true;
                assignedOrders = new[] { activeOrder }
                    .Concat(assignedOrders)
                    .GroupBy(row => row.OperationId)
                    .Select(group => group.First())
                    .ToList();
            }
        }

        foreach (var order in assignedOrders)
        {
            order.IsActive = string.Equals(order.OrderCode, activeOrderCode, StringComparison.OrdinalIgnoreCase)
                             && (string.IsNullOrWhiteSpace(activeOperationCode)
                                 || string.Equals(order.OperationCode, activeOperationCode, StringComparison.OrdinalIgnoreCase));
        }

        var orderCounters =
            !string.IsNullOrWhiteSpace(activeOrderCode)
                ? await LoadOrderCountersAsync(
                    code,
                    activeOrderCode,
                    activeOperationCode,
                    cancellationToken)
                : new MesWorkcenterOrderCounterSummary();

        IReadOnlyList<MesWorkcenterOperatorRecord> operators =
            context is not null && context.MesId != Guid.Empty
                ? await LoadCurrentOperatorsAsync(context.MesId, context.ShiftName, cancellationToken)
                : Array.Empty<MesWorkcenterOperatorRecord>();

        var now = DateTime.Now;
        var shiftStart = live?.ShiftStartTime ?? context?.ShiftStarttime;
        var shiftEnd = live?.ShiftEndTime ?? context?.ShiftEndtime;
        var shiftName = !string.IsNullOrWhiteSpace(live?.ShiftName)
            ? live!.ShiftName
            : context?.ShiftName ?? string.Empty;

        Mes06OeeReportRecord? oee = null;
        var shiftMetrics = new MesWorkcenterShiftMetrics();
        IReadOnlyList<Mes06ProductionGraphRecord> graphRows = Array.Empty<Mes06ProductionGraphRecord>();
        IReadOnlyList<MesWorkcenterDowntimeRecord> stateSummary = Array.Empty<MesWorkcenterDowntimeRecord>();

        if (shiftStart.HasValue && shiftEnd.HasValue && shiftEnd.Value > shiftStart.Value)
        {
            var reportTo =
                now < shiftEnd.Value
                    ? now
                    : shiftEnd.Value;

            if (reportTo > shiftStart.Value)
            {
                var shiftId =
                    live?.ShiftId
                    ?? context?.ShiftId;

                var shiftMetricsTask =
                    LoadCurrentShiftMetricsAsync(
                        code,
                        shiftId,
                        shiftStart.Value,
                        reportTo,
                        cancellationToken);

                var graphTask =
                    _reportingService.GetProductionGraphRowsAsync(
                        shiftStart.Value,
                        reportTo,
                        code,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        cancellationToken);

                if (_stateColors is null)
                {
                    _stateColors =
                        await _reportingService.GetStateColorsAsync(
                            cancellationToken);
                }

                await Task.WhenAll(
                    shiftMetricsTask,
                    graphTask);

                shiftMetrics =
                    shiftMetricsTask.Result;

                graphRows =
                    graphTask.Result;

                stateSummary =
                    BuildStateSummary(
                        code,
                        graphRows,
                        _stateColors);
            }
        }

        return new MesWorkcenterDashboardSnapshot
        {
            WorkcenterCode = code,
            WorkcenterDescription = live?.WorkcenterDescription ?? string.Empty,
            LoadedAt = now,
            Live = live,
            CurrentContext = context,
            ActiveOrder = activeOrder,
            AssignedOrders = assignedOrders
                .OrderByDescending(row => row.IsActive)
                .ThenBy(row => row.PlannedStart ?? DateTime.MaxValue)
                .ThenBy(row => row.OrderCode, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Operators = operators,
            Oee = oee,
            ShiftMetrics = shiftMetrics,
            GraphRows = graphRows,
            StateColors = _stateColors ?? Array.Empty<MesReportingStateColor>(),
            StateSummary = stateSummary,
            OrderCounters = orderCounters
        };
    }


    private async Task<MesWorkcenterShiftMetrics> LoadCurrentShiftMetricsAsync(
        string workcenterCode,
        Guid? shiftId,
        DateTime shiftStart,
        DateTime reportTo,
        CancellationToken cancellationToken)
    {
        var sql =
            $"""
            SELECT
                mes.[DurationUnoccupied],
                mes.[DurationUtilization],
                mes.[DurationDown],
                mes.[PerformanceTotal],
                mes.[PerformanceGood],
                mes.[PerformanceBad],
                mes.[PerformanceRework],
                op.[ExpPerformance],
                op.[ProcessingTime]
            FROM [{_analyticsSchema}].[FactMdaMes] mes
            INNER JOIN [{_analyticsSchema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            WHERE wc.[Code] = @workcenterCode
              AND
              (
                    (
                        @shiftId IS NOT NULL
                        AND mes.[ShiftID] = @shiftId
                    )
                    OR
                    (
                        @shiftId IS NULL
                        AND mes.[Starttime] < @reportTo
                        AND COALESCE(mes.[Endtime], GETDATE()) > @shiftStart
                    )
              )
            ORDER BY
                mes.[Starttime],
                mes.[ID];
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            CreateCommand(
                connection,
                sql);

        AddParameter(
            command,
            "@workcenterCode",
            workcenterCode);

        AddParameter(
            command,
            "@shiftId",
            shiftId);

        AddParameter(
            command,
            "@shiftStart",
            shiftStart);

        AddParameter(
            command,
            "@reportTo",
            reportTo);

        double plannedShutdownSeconds = 0d;
        double availableSeconds = 0d;
        double failureSeconds = 0d;

        decimal total = 0m;
        decimal good = 0m;
        decimal bad = 0m;
        decimal rework = 0m;

        decimal weightedPlanned = 0m;
        decimal plannedWeight = 0m;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var rowAvailable =
                GetDecimal(
                    reader,
                    "DurationUtilization")
                ?? 0m;

            plannedShutdownSeconds +=
                (double)(
                    GetDecimal(
                        reader,
                        "DurationUnoccupied")
                    ?? 0m);

            availableSeconds +=
                (double)rowAvailable;

            failureSeconds +=
                (double)(
                    GetDecimal(
                        reader,
                        "DurationDown")
                    ?? 0m);

            total +=
                GetDecimal(
                    reader,
                    "PerformanceTotal")
                ?? 0m;

            good +=
                GetDecimal(
                    reader,
                    "PerformanceGood")
                ?? 0m;

            bad +=
                GetDecimal(
                    reader,
                    "PerformanceBad")
                ?? 0m;

            rework +=
                GetDecimal(
                    reader,
                    "PerformanceRework")
                ?? 0m;

            var planned =
                GetDecimal(
                    reader,
                    "ExpPerformance");

            if ((!planned.HasValue || planned.Value <= 0m)
                && GetDecimal(
                       reader,
                       "ProcessingTime") is decimal processingTime
                && processingTime > 0m)
            {
                planned =
                    60m / processingTime;
            }

            if (planned.HasValue
                && planned.Value > 0m
                && rowAvailable > 0m)
            {
                weightedPlanned +=
                    planned.Value
                    * rowAvailable;

                plannedWeight +=
                    rowAvailable;
            }
        }

        return new MesWorkcenterShiftMetrics
        {
            PlannedShutdownSeconds =
                plannedShutdownSeconds,
            AvailableSeconds =
                availableSeconds,
            FailureSeconds =
                failureSeconds,
            PerformanceTotal =
                total,
            PerformanceGood =
                good,
            PerformanceBad =
                bad,
            PerformanceRework =
                rework,
            PlannedPerformance =
                plannedWeight > 0m
                    ? weightedPlanned / plannedWeight
                    : null
        };
    }

    private async Task<MesWorkcenterOrderCounterSummary> LoadOrderCountersAsync(
        string workcenterCode,
        string orderCode,
        string operationCode,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                dc.[Name] AS CounterName,
                SUM(COALESCE(c.[Value], 0)) AS CounterValue
            FROM [{_analyticsSchema}].[FactMdaCounter] c
            INNER JOIN [{_analyticsSchema}].[DimMdaCounter] dc
                ON dc.[ID] = c.[CounterID]
            INNER JOIN [{_analyticsSchema}].[FactMdaMes] mes
                ON mes.[ID] = c.[MesID]
            INNER JOIN [{_analyticsSchema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            WHERE wc.[Code] = @workcenterCode
              AND op.[OrderCode] = @orderCode
              AND (
                    @operationCode = ''
                    OR op.[OperationCode] = @operationCode
                  )
            GROUP BY dc.[Name]
            ORDER BY dc.[Name];
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = CreateCommand(connection, sql);
        AddParameter(command, "@workcenterCode", workcenterCode);
        AddParameter(command, "@orderCode", orderCode);
        AddParameter(command, "@operationCode", operationCode ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        decimal scrapProduction = 0m;
        decimal scrapGlass = 0m;
        decimal developmentDepartment = 0m;
        decimal qualityDepartment = 0m;
        decimal setup = 0m;
        decimal washedBottles = 0m;
        decimal transportLogistics = 0m;

        while (await reader.ReadAsync(cancellationToken))
        {
            var counterName = GetString(reader, "CounterName");
            var value = GetDecimal(reader, "CounterValue") ?? 0m;

            switch (NormalizeCounterName(counterName))
            {
                case "odpadprodukce":
                    scrapProduction += value;
                    break;
                case "odpadsklo":
                    scrapGlass += value;
                    break;
                case "oddelenivyvoje":
                    developmentDepartment += value;
                    break;
                case "oddelenikvality":
                    qualityDepartment += value;
                    break;
                case "serizovani":
                    setup += value;
                    break;
                case "myteflakony":
                    washedBottles += value;
                    break;
                case "transportlogistika":
                    transportLogistics += value;
                    break;
            }
        }

        return new MesWorkcenterOrderCounterSummary
        {
            ScrapProduction = scrapProduction,
            ScrapGlass = scrapGlass,
            DevelopmentDepartment = developmentDepartment,
            QualityDepartment = qualityDepartment,
            Setup = setup,
            WashedBottles = washedBottles,
            TransportLogistics = transportLogistics
        };
    }

    private static string NormalizeCounterName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task<MesWorkcenterCurrentContext?> LoadCurrentContextAsync(
        string workcenterCode,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT TOP (1)
                m.[ID] AS MesID,
                m.[ShiftID],
                m.[OperationID],
                m.[Starttime] AS MesStarttime,
                op.[OperationCode],
                op.[OrderCode],
                op.[ProductCode],
                op.[ProductDescription],
                sh.[Name] AS ShiftName,
                sh.[Starttime] AS ShiftStarttime,
                sh.[Endtime] AS ShiftEndtime
            FROM [{_analyticsSchema}].[FactMdaMes] m
            INNER JOIN [{_analyticsSchema}].[DimWorkcenter] w
                ON w.[ID] = m.[WorkcenterID]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = m.[OperationID]
            LEFT JOIN [{_analyticsSchema}].[DimShiftEvent] sh
                ON sh.[ID] = m.[ShiftID]
            WHERE w.[Code] = @workcenterCode
              AND m.[Endtime] IS NULL
            ORDER BY m.[Starttime] DESC, m.[ID] DESC;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddParameter(command, "@workcenterCode", workcenterCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new MesWorkcenterCurrentContext
        {
            MesId = GetGuid(reader, "MesID") ?? Guid.Empty,
            ShiftId = GetGuid(reader, "ShiftID"),
            OperationId = GetGuid(reader, "OperationID"),
            OperationCode = GetString(reader, "OperationCode"),
            OrderCode = GetString(reader, "OrderCode"),
            ProductCode = GetString(reader, "ProductCode"),
            ProductDescription = GetString(reader, "ProductDescription"),
            ShiftName = GetString(reader, "ShiftName"),
            ShiftStarttime = GetDateTime(reader, "ShiftStarttime"),
            ShiftEndtime = GetDateTime(reader, "ShiftEndtime"),
            MesStarttime = GetDateTime(reader, "MesStarttime")
        };
    }

    private async Task<IReadOnlyList<MesWorkcenterOperatorRecord>> LoadCurrentOperatorsAsync(
        Guid mesId,
        string shiftName,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT DISTINCT
                h.[HumanCode],
                h.[FirstName],
                h.[LastName],
                h.[Starttime],
                h.[Endtime]
            FROM [{_analyticsSchema}].[FactHResLinkExt] h
            WHERE h.[MesID] = @mesId
              AND h.[Starttime] <= GETDATE()
              AND COALESCE(h.[Endtime], CONVERT(datetime, '9999-12-31T23:59:59')) > GETDATE()
              AND (
                    NULLIF(LTRIM(RTRIM(h.[HumanCode])), '') IS NOT NULL
                    OR NULLIF(LTRIM(RTRIM(h.[FirstName])), '') IS NOT NULL
                    OR NULLIF(LTRIM(RTRIM(h.[LastName])), '') IS NOT NULL
                  )
            ORDER BY h.[Starttime], h.[LastName], h.[FirstName];
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddParameter(command, "@mesId", mesId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<MesWorkcenterOperatorRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MesWorkcenterOperatorRecord
            {
                HumanCode = GetString(reader, "HumanCode"),
                FirstName = GetString(reader, "FirstName"),
                LastName = GetString(reader, "LastName"),
                LoginTime = GetDateTime(reader, "Starttime"),
                ShiftName = shiftName
            });
        }

        return rows
            .GroupBy(row => $"{row.HumanCode}\u001F{row.FirstName}\u001F{row.LastName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(row => row.LoginTime ?? DateTime.MaxValue).First())
            .OrderBy(row => row.LoginTime ?? DateTime.MaxValue)
            .ToList();
    }

    private async Task<IReadOnlyList<MesWorkcenterOrderRecord>> LoadAssignedOrdersAsync(
        string workcenterCode,
        string activeOrderCode,
        string activeOperationCode,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT TOP (60)
                po.[id] AS ProductionOrderID,
                r.[id] AS OperationID,
                po.[code] AS OrderCode,
                po.[product_code] AS ProductCode,
                po.[product_description] AS ProductDescription,
                po.[cf_customer] AS SapArticleNumber,
                po.[general_status] AS GeneralStatus,
                po.[production_status] AS ProductionStatus,
                r.[code] AS OperationCode,
                r.[description] AS RoutingDescription,
                COALESCE(r.[quantity], 0) AS TargetQuantity,
                COALESCE(r.[finished_quantity], 0) AS FinishedQuantity,
                COALESCE(r.[scrap_quantity], 0) AS ScrapQuantity,
                CASE
                    WHEN r.[exp_performance] IS NOT NULL AND r.[exp_performance] > 0
                    THEN r.[exp_performance]
                    WHEN r.[processing_time] IS NOT NULL AND r.[processing_time] > 0
                    THEN 60.0 / r.[processing_time]
                    ELSE NULL
                END AS PlannedPerformance,
                r.[planned_start_date] AS PlannedStart,
                r.[planned_end_date] AS PlannedEnd,
                r.[actual_start_date] AS ActualStart,
                r.[actual_end_date] AS ActualEnd
            FROM dbo.[d_pda_po] po
            INNER JOIN dbo.[d_pda_po_routing] r
                ON r.[production_order_id] = po.[id]
            WHERE COALESCE(po.[archive_status], 0) = 0
              AND (
                    NULLIF(LTRIM(RTRIM(r.[cost_center])), '') = @workcenterCode
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.[d_dp_routing] planning
                        INNER JOIN dbo.[d_dp_routing_pres_link] link
                            ON link.[planning_routing_id] = planning.[id]
                        INNER JOIN dbo.[m_res_resource] resource
                            ON resource.[id] = link.[resource_id]
                        WHERE planning.[operation_id] = r.[id]
                          AND resource.[code] = @workcenterCode
                    )
                  )
            ORDER BY
                CASE
                    WHEN po.[code] = @activeOrderCode
                     AND (@activeOperationCode = '' OR r.[code] = @activeOperationCode)
                    THEN 0 ELSE 1
                END,
                CASE WHEN r.[planned_start_date] IS NULL THEN 1 ELSE 0 END,
                r.[planned_start_date],
                po.[code],
                TRY_CONVERT(int, r.[code]),
                r.[code];
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddParameter(command, "@workcenterCode", workcenterCode);
        AddParameter(command, "@activeOrderCode", activeOrderCode ?? string.Empty);
        AddParameter(command, "@activeOperationCode", activeOperationCode ?? string.Empty);
        return await ReadOrdersAsync(command, cancellationToken);
    }

    private async Task<MesWorkcenterOrderRecord?> LoadOrderOperationAsync(
        string orderCode,
        string operationCode,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT TOP (1)
                po.[id] AS ProductionOrderID,
                r.[id] AS OperationID,
                po.[code] AS OrderCode,
                po.[product_code] AS ProductCode,
                po.[product_description] AS ProductDescription,
                po.[cf_customer] AS SapArticleNumber,
                po.[general_status] AS GeneralStatus,
                po.[production_status] AS ProductionStatus,
                r.[code] AS OperationCode,
                r.[description] AS RoutingDescription,
                COALESCE(r.[quantity], 0) AS TargetQuantity,
                COALESCE(r.[finished_quantity], 0) AS FinishedQuantity,
                COALESCE(r.[scrap_quantity], 0) AS ScrapQuantity,
                CASE
                    WHEN r.[exp_performance] IS NOT NULL AND r.[exp_performance] > 0
                    THEN r.[exp_performance]
                    WHEN r.[processing_time] IS NOT NULL AND r.[processing_time] > 0
                    THEN 60.0 / r.[processing_time]
                    ELSE NULL
                END AS PlannedPerformance,
                r.[planned_start_date] AS PlannedStart,
                r.[planned_end_date] AS PlannedEnd,
                r.[actual_start_date] AS ActualStart,
                r.[actual_end_date] AS ActualEnd
            FROM dbo.[d_pda_po] po
            INNER JOIN dbo.[d_pda_po_routing] r
                ON r.[production_order_id] = po.[id]
            WHERE po.[code] = @orderCode
              AND (@operationCode = '' OR r.[code] = @operationCode)
            ORDER BY
                CASE WHEN @operationCode <> '' AND r.[code] = @operationCode THEN 0 ELSE 1 END,
                TRY_CONVERT(int, r.[code]),
                r.[code];
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddParameter(command, "@orderCode", orderCode);
        AddParameter(command, "@operationCode", operationCode ?? string.Empty);
        var rows = await ReadOrdersAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<MesWorkcenterOrderRecord>> ReadOrdersAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MesWorkcenterOrderRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MesWorkcenterOrderRecord
            {
                ProductionOrderId = GetGuid(reader, "ProductionOrderID") ?? Guid.Empty,
                OperationId = GetGuid(reader, "OperationID") ?? Guid.Empty,
                OrderCode = GetString(reader, "OrderCode"),
                ProductCode = GetString(reader, "ProductCode"),
                ProductDescription = GetString(reader, "ProductDescription"),
                SapArticleNumber = GetString(reader, "SapArticleNumber"),
                GeneralStatus = GetInt(reader, "GeneralStatus"),
                ProductionStatus = GetInt(reader, "ProductionStatus"),
                OperationCode = GetString(reader, "OperationCode"),
                RoutingDescription = GetString(reader, "RoutingDescription"),
                TargetQuantity = GetDecimal(reader, "TargetQuantity") ?? 0m,
                FinishedQuantity = GetDecimal(reader, "FinishedQuantity") ?? 0m,
                ScrapQuantity = GetDecimal(reader, "ScrapQuantity") ?? 0m,
                PlannedPerformance = GetDecimal(reader, "PlannedPerformance"),
                PlannedStart = GetDateTime(reader, "PlannedStart"),
                PlannedEnd = GetDateTime(reader, "PlannedEnd"),
                ActualStart = GetDateTime(reader, "ActualStart"),
                ActualEnd = GetDateTime(reader, "ActualEnd")
            });
        }

        return rows;
    }

    private static IReadOnlyList<MesWorkcenterDowntimeRecord> BuildStateSummary(
        string workcenterCode,
        IReadOnlyList<Mes06ProductionGraphRecord> rows,
        IReadOnlyList<MesReportingStateColor> colors)
    {
        return rows
            .Where(row => row.Endtime > row.Starttime)
            .GroupBy(row => row.StateName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var name = string.IsNullOrWhiteSpace(group.Key) ? "—" : group.Key;
                var color = colors.FirstOrDefault(item =>
                                string.Equals(item.WorkcenterCode, workcenterCode, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(item.StateName, name, StringComparison.CurrentCultureIgnoreCase))
                            ?? colors.FirstOrDefault(item =>
                                string.Equals(item.StateName, name, StringComparison.CurrentCultureIgnoreCase));

                var seconds = group.Sum(row => Math.Max(0d, (row.Endtime - row.Starttime).TotalSeconds));

                return new MesWorkcenterDowntimeRecord
                {
                    StateName = name,
                    Occurrences = group.Count(),
                    DurationSeconds = seconds,
                    Color = !string.IsNullOrWhiteSpace(color?.StateColor)
                        ? color!.StateColor
                        : color?.CategoryColor ?? string.Empty
                };
            })
            .OrderByDescending(row => row.DurationSeconds)
            .ThenBy(row => row.StateName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private DbConnection CreateConnection()
    {
        var directConnectionString = ReadConnectionString();
        if (!string.IsNullOrWhiteSpace(directConnectionString))
        {
            return CreateSqlServerConnection(directConnectionString);
        }

        var server = ReadString("Server", "SqlServer", "ServerName", "DataSource", "Host", "Address", "ServerAddress");
        var database = ReadString("Database", "DatabaseName", "InitialCatalog", "Catalog");

        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("MESSET does not contain a SQL server address.");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("MESSET does not contain a SQL database name.");
        }

        var encrypt = ReadBool(true, "Encrypt", "UseEncryption");
        var trust = ReadBool(true, "TrustServerCertificate", "TrustCertificate");
        var timeout = Math.Max(2, ReadInt(8, "ConnectionTimeoutSeconds", "ConnectTimeoutSeconds", "ConnectionTimeout"));
        var connectionString = $"Server={server};Database={database};Integrated Security=True;Encrypt={encrypt};TrustServerCertificate={trust};Connect Timeout={timeout};Application Name=DMS MESWC;";
        return CreateSqlServerConnection(connectionString);
    }

    private string? ReadConnectionString()
    {
        var propertyValue = ReadString("ConnectionString", "SqlConnectionString");
        if (!string.IsNullOrWhiteSpace(propertyValue))
        {
            return propertyValue;
        }

        foreach (var methodName in new[] { "BuildConnectionString", "CreateConnectionString", "GetConnectionString" })
        {
            var method = _settings.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (method?.ReturnType == typeof(string)
                && method.Invoke(_settings, null) is string value
                && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static DbConnection CreateSqlServerConnection(string connectionString)
    {
        var type = Type.GetType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient", throwOnError: false)
                   ?? Type.GetType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient", throwOnError: false)
                   ?? throw new InvalidOperationException("SQL Server provider is not available.");

        return (DbConnection)(Activator.CreateInstance(type, connectionString)
                              ?? throw new InvalidOperationException("SQL Server connection could not be created."));
    }

    private DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _commandTimeoutSeconds;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private string? ReadString(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = _settings.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property?.GetValue(_settings) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private int ReadInt(int fallback, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = _settings.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var value = property?.GetValue(_settings);

            if (value is null)
            {
                continue;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                // Try next property name.
            }
        }

        return fallback;
    }

    private bool ReadBool(bool fallback, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = _settings.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var value = property?.GetValue(_settings);

            if (value is bool boolean)
            {
                return boolean;
            }

            if (value is not null
                && bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static int GetInt(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static string GetString(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Guid? GetGuid(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is Guid guid)
        {
            return guid;
        }

        return Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? GetDateTime(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        return DateTime.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;
    }

    private static decimal? GetDecimal(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }
}
