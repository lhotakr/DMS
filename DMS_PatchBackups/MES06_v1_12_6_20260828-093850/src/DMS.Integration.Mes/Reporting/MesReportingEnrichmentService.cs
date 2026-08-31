using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting;

/// <summary>
/// Small read-only enrichment layer for MES06.
/// It deliberately does not replace MesReportingDataService:
/// - shift events are read from ana.DimShiftEvent
/// - SAP article number is read from dbo.d_pda_po.cf_customer
/// </summary>
public sealed class MesReportingEnrichmentService
{
    private readonly object _settings;
    private readonly string _analyticsSchema;
    private readonly int _commandTimeoutSeconds;

    public MesReportingEnrichmentService(
        object settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));

        _analyticsSchema =
            ReadString(
                "ReportingSchema",
                "Schema",
                "SchemaName")
            ?? "ana";

        _commandTimeoutSeconds =
            Math.Max(
                5,
                ReadInt(
                    30,
                    "CommandTimeoutSeconds",
                    "SqlCommandTimeoutSeconds",
                    "CommandTimeout"));
    }

    public async Task<(DateTime From, DateTime To)> GetShiftRelatedPeriodAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var selectedFrom =
            fromDate.Date;

        var selectedTo =
            toDate.Date;

        if (selectedTo <=
            selectedFrom)
        {
            selectedTo =
                selectedFrom.AddDays(
                    1);
        }

        var probeFrom =
            selectedFrom.AddDays(
                -14);

        var probeTo =
            selectedTo.AddDays(
                14);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        var sql =
            $"""
            SELECT
                (
                    SELECT MIN(s.[Starttime])
                    FROM [{_analyticsSchema}].[DimShiftEvent] s
                    WHERE s.[Starttime] >= @fromDate
                      AND s.[Starttime] < DATEADD(DAY, 1, @fromDate)
                ) AS ActualFrom,

                (
                    SELECT MIN(s.[Starttime])
                    FROM [{_analyticsSchema}].[DimShiftEvent] s
                    WHERE s.[Starttime] >= @toDate
                      AND s.[Starttime] < DATEADD(DAY, 1, @toDate)
                ) AS ActualTo,

                (
                    SELECT TOP (1)
                        DATEPART(HOUR, s.[Starttime]) * 60
                        + DATEPART(MINUTE, s.[Starttime])
                    FROM [{_analyticsSchema}].[DimShiftEvent] s
                    WHERE s.[Starttime] >= @probeFrom
                      AND s.[Starttime] < @probeTo
                    GROUP BY
                        DATEPART(HOUR, s.[Starttime]) * 60
                        + DATEPART(MINUTE, s.[Starttime])
                    ORDER BY
                        COUNT_BIG(*) DESC,
                        DATEPART(HOUR, s.[Starttime]) * 60
                        + DATEPART(MINUTE, s.[Starttime])
                ) AS TypicalStartMinute;
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        AddParameter(
            command,
            "@fromDate",
            selectedFrom);

        AddParameter(
            command,
            "@toDate",
            selectedTo);

        AddParameter(
            command,
            "@probeFrom",
            probeFrom);

        AddParameter(
            command,
            "@probeTo",
            probeTo);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return (
                selectedFrom,
                selectedTo);
        }

        var actualFrom =
            GetDateTime(
                reader,
                "ActualFrom");

        var actualTo =
            GetDateTime(
                reader,
                "ActualTo");

        int? typicalStartMinute =
            null;

        var typicalOrdinal =
            reader.GetOrdinal(
                "TypicalStartMinute");

        if (!reader.IsDBNull(
                typicalOrdinal))
        {
            typicalStartMinute =
                Convert.ToInt32(
                    reader.GetValue(
                        typicalOrdinal),
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        var fallbackOffset =
            TimeSpan.FromMinutes(
                typicalStartMinute
                ?? 0);

        var periodFrom =
            actualFrom
            ?? selectedFrom.Add(
                fallbackOffset);

        var periodTo =
            actualTo
            ?? selectedTo.Add(
                fallbackOffset);

        if (periodTo <=
            periodFrom)
        {
            periodTo =
                periodFrom.AddDays(
                    1);
        }

        return (
            periodFrom,
            periodTo);
    }

    public async Task<IReadOnlyList<Mes06OeeReportRecord>> GetOeeReportAsync(
        DateTime from,
        DateTime to,
        IReadOnlyList<string> workcenterCodes,
        string shiftCode,
        string orderCode,
        string operationCode,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            to = from.AddDays(1);
        }

        var selectedWorkcenters =
            workcenterCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToList();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;

        var wcParameters = new List<string>();
        for (var index = 0; index < selectedWorkcenters.Count; index++)
        {
            var parameterName = $"@oeeWc{index}";
            wcParameters.Add(parameterName);
            AddParameter(command, parameterName, selectedWorkcenters[index]);
        }

        var workcenterClause =
            wcParameters.Count > 0
                ? $"AND w.[Code] IN ({string.Join(", ", wcParameters)})"
                : string.Empty;

        command.CommandText =
            $"""
            SELECT
                mes.[ID] AS MesID,
                w.[Code] AS WorkcenterCode,
                mes.[Starttime],
                mes.[Endtime],
                mes.[DurationUnoccupied],
                mes.[DurationUtilization],
                mes.[DurationDown],
                mes.[PerformanceTotal],
                mes.[PerformanceGood],
                mes.[PerformanceBad],
                mes.[PerformanceRework],
                op.[ExpPerformance],
                op.[ProcessingTime],
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                h.[HumanCode],
                h.[FirstName],
                h.[LastName],
                h.[Starttime] AS HumanStarttime,
                h.[Endtime] AS HumanEndtime,
                se.[Name] AS ShiftName,
                se.[Starttime] AS ShiftStarttime
            FROM [{_analyticsSchema}].[FactMdaMes] mes
            INNER JOIN [{_analyticsSchema}].[DimWorkcenter] w
                ON w.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_analyticsSchema}].[FactHResLinkExt] h
                ON h.[MesID] = mes.[ID]
            LEFT JOIN [{_analyticsSchema}].[DimShiftEvent] se
                ON se.[ID] = mes.[ShiftID]
            WHERE mes.[Starttime] < @to
              AND mes.[Endtime] > @from
              {workcenterClause}
              AND (@shiftCode = '' OR se.[Name] = @shiftCode)
              AND (@orderCode = '' OR op.[OrderCode] LIKE '%' + @orderCode + '%')
              AND (@operationCode = '' OR op.[OperationCode] LIKE '%' + @operationCode + '%')
              AND (@productCode = '' OR op.[ProductCode] LIKE '%' + @productCode + '%')
            ORDER BY
                w.[Code],
                se.[Starttime],
                mes.[Starttime],
                h.[LastName],
                h.[FirstName];
            """;

        AddParameter(command, "@from", from);
        AddParameter(command, "@to", to);
        AddParameter(command, "@shiftCode", shiftCode?.Trim() ?? string.Empty);
        AddParameter(command, "@orderCode", orderCode?.Trim() ?? string.Empty);
        AddParameter(command, "@operationCode", operationCode?.Trim() ?? string.Empty);
        AddParameter(command, "@productCode", productCode?.Trim() ?? string.Empty);

        // One MesID may have several FactHResLinkExt rows. OEE/MDA values belong
        // to the MES period itself, so count them once and collect personnel separately.
        var mesPeriods =
            new Dictionary<Guid, Mes06OeeMesPeriod>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var mesId = GetGuid(reader, "MesID") ?? Guid.Empty;
            var mesStart = GetDateTime(reader, "Starttime");
            var mesEnd = GetDateTime(reader, "Endtime");

            if (mesId == Guid.Empty
                || !mesStart.HasValue
                || !mesEnd.HasValue
                || mesEnd.Value <= mesStart.Value)
            {
                continue;
            }

            var clippedStart = mesStart.Value < from ? from : mesStart.Value;
            var clippedEnd = mesEnd.Value > to ? to : mesEnd.Value;
            if (clippedEnd <= clippedStart)
            {
                continue;
            }

            var rawSeconds = (mesEnd.Value - mesStart.Value).TotalSeconds;
            var clippedSeconds = (clippedEnd - clippedStart).TotalSeconds;
            if (rawSeconds <= 0d || clippedSeconds <= 0d)
            {
                continue;
            }

            if (!mesPeriods.TryGetValue(mesId, out var period))
            {
                var periodFraction = Math.Clamp(clippedSeconds / rawSeconds, 0d, 1d);
                var planned = GetDecimal(reader, "ExpPerformance");
                if ((!planned.HasValue || planned.Value <= 0m)
                    && GetDecimal(reader, "ProcessingTime") is decimal processingTime
                    && processingTime > 0m)
                {
                    planned = 60m / processingTime;
                }

                period = new Mes06OeeMesPeriod
                {
                    MesId = mesId,
                    WorkcenterCode = GetString(reader, "WorkcenterCode"),
                    ShiftName = GetString(reader, "ShiftName"),
                    ShiftStarttime = GetDateTime(reader, "ShiftStarttime"),
                    Starttime = clippedStart,
                    Endtime = clippedEnd,
                    PlannedShutdownSeconds = (double)(GetDecimal(reader, "DurationUnoccupied") ?? 0m) * periodFraction,
                    AvailableSeconds = (double)(GetDecimal(reader, "DurationUtilization") ?? 0m) * periodFraction,
                    FailureSeconds = (double)(GetDecimal(reader, "DurationDown") ?? 0m) * periodFraction,
                    TotalAmount = (double)(GetDecimal(reader, "PerformanceTotal") ?? 0m) * periodFraction,
                    GoodUnits = (double)(GetDecimal(reader, "PerformanceGood") ?? 0m) * periodFraction,
                    BadUnits = (double)(GetDecimal(reader, "PerformanceBad") ?? 0m) * periodFraction,
                    ReworkUnits = (double)(GetDecimal(reader, "PerformanceRework") ?? 0m) * periodFraction,
                    PlannedPerformance = planned.HasValue && planned.Value > 0m
                        ? (double)planned.Value
                        : 0d
                };

                mesPeriods[mesId] = period;
            }

            var humanStart = GetDateTime(reader, "HumanStarttime") ?? period.Starttime;
            var humanEnd = GetDateTime(reader, "HumanEndtime") ?? period.Endtime;
            var overlapStart = humanStart > period.Starttime ? humanStart : period.Starttime;
            var overlapEnd = humanEnd < period.Endtime ? humanEnd : period.Endtime;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var personnel = Mes06OeeReportRecord.FormatPersonnel(
                GetString(reader, "LastName"),
                GetString(reader, "FirstName"),
                GetString(reader, "HumanCode"));

            if (!string.IsNullOrWhiteSpace(personnel) && personnel != "—")
            {
                period.Personnel.Add(personnel);
            }
        }

        var accumulators =
            new Dictionary<string, Mes06OeeAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var period in mesPeriods.Values)
        {
            // Final row = Workcenter + Shift.
            var key = $"{period.WorkcenterCode}\u001F{period.ShiftName}";

            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new Mes06OeeAccumulator
                {
                    WorkcenterCode = period.WorkcenterCode,
                    ShiftName = period.ShiftName,
                    ShiftStarttime = period.ShiftStarttime
                };
                accumulators[key] = accumulator;
            }
            else if (period.ShiftStarttime.HasValue
                     && (!accumulator.ShiftStarttime.HasValue
                         || period.ShiftStarttime.Value < accumulator.ShiftStarttime.Value))
            {
                accumulator.ShiftStarttime = period.ShiftStarttime;
            }

            accumulator.PlannedShutdownSeconds += period.PlannedShutdownSeconds;
            accumulator.AvailableSeconds += period.AvailableSeconds;
            accumulator.FailureSeconds += period.FailureSeconds;
            accumulator.TotalAmount += period.TotalAmount;
            accumulator.GoodUnits += period.GoodUnits;
            accumulator.BadUnits += period.BadUnits;
            accumulator.ReworkUnits += period.ReworkUnits;

            if (period.PlannedPerformance > 0d && period.AvailableSeconds > 0d)
            {
                accumulator.PlannedPerformanceWeighted += period.PlannedPerformance * period.AvailableSeconds;
                accumulator.PlannedPerformanceWeight += period.AvailableSeconds;
            }

            foreach (var person in period.Personnel)
            {
                accumulator.Personnel.Add(person);
            }
        }

        return accumulators.Values
            .Select(Mes06OeeReportRecord.FromAccumulator)
            .OrderBy(row => row.WorkcenterCode, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.ShiftStarttime ?? DateTime.MaxValue)
            .ThenBy(row => row.ShiftName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<Mes06ProcessValueRecord>> GetProcessValuesAsync(
        DateTime from,
        DateTime to,
        IReadOnlyList<string> workcenterCodes,
        string shiftCode,
        string orderCode,
        string operationCode,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            to = from.AddDays(1);
        }

        var selectedWorkcenters =
            workcenterCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToList();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;

        var wcParameters = new List<string>();
        for (var index = 0; index < selectedWorkcenters.Count; index++)
        {
            var parameterName = $"@pmWc{index}";
            wcParameters.Add(parameterName);
            AddParameter(command, parameterName, selectedWorkcenters[index]);
        }

        var workcenterClause =
            wcParameters.Count > 0
                ? $"AND w.[Code] IN ({string.Join(", ", wcParameters)})"
                : string.Empty;

        command.CommandText =
            $"""
            SELECT TOP (20000)
                pm.[id] AS ID,
                pm.[mes_id] AS MesID,
                pm.[text] AS StateName,
                pm.[starttime] AS Starttime,
                pm.[endtime] AS Endtime,
                w.[Code] AS WorkcenterCode,
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                se.[Name] AS ShiftName
            FROM dbo.[d_mda_processmessage] pm
            LEFT JOIN [{_analyticsSchema}].[FactMdaMes] mes
                ON mes.[ID] = pm.[mes_id]
            LEFT JOIN [{_analyticsSchema}].[DimWorkcenter] w
                ON w.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_analyticsSchema}].[DimShiftEvent] se
                ON se.[ID] = mes.[ShiftID]
            WHERE pm.[starttime] < @to
              AND COALESCE(pm.[endtime], @to) > @from
              {workcenterClause}
              AND (@shiftCode = '' OR se.[Name] = @shiftCode)
              AND (@orderCode = '' OR op.[OrderCode] LIKE '%' + @orderCode + '%')
              AND (@operationCode = '' OR op.[OperationCode] LIKE '%' + @operationCode + '%')
              AND (@productCode = '' OR op.[ProductCode] LIKE '%' + @productCode + '%')
            ORDER BY
                w.[Code],
                pm.[text],
                pm.[starttime];
            """;

        AddParameter(command, "@from", from);
        AddParameter(command, "@to", to);
        AddParameter(command, "@shiftCode", shiftCode?.Trim() ?? string.Empty);
        AddParameter(command, "@orderCode", orderCode?.Trim() ?? string.Empty);
        AddParameter(command, "@operationCode", operationCode?.Trim() ?? string.Empty);
        AddParameter(command, "@productCode", productCode?.Trim() ?? string.Empty);

        var rows = new List<Mes06ProcessValueRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var start = GetDateTime(reader, "Starttime");
            if (!start.HasValue)
            {
                continue;
            }

            var end = GetDateTime(reader, "Endtime") ?? to;
            var clippedStart = start.Value < from ? from : start.Value;
            var clippedEnd = end > to ? to : end;
            if (clippedEnd <= clippedStart)
            {
                continue;
            }

            rows.Add(new Mes06ProcessValueRecord
            {
                Id = GetGuid(reader, "ID") ?? Guid.Empty,
                MesId = GetGuid(reader, "MesID") ?? Guid.Empty,
                WorkcenterCode = GetString(reader, "WorkcenterCode"),
                StateName = GetString(reader, "StateName"),
                Starttime = clippedStart,
                Endtime = clippedEnd,
                OrderCode = GetString(reader, "OrderCode"),
                OperationCode = GetString(reader, "OperationCode"),
                ProductCode = GetString(reader, "ProductCode"),
                ShiftName = GetString(reader, "ShiftName")
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<Mes06ProductionGraphRecord>> GetProductionGraphRowsAsync(
        DateTime from,
        DateTime to,
        string workcenterCode,
        string orderCode,
        string operationCode,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            to =
                from.AddDays(
                    1);
        }

        if (string.IsNullOrWhiteSpace(
                workcenterCode))
        {
            return Array.Empty<Mes06ProductionGraphRecord>();
        }

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        var sql =
            $"""
            SELECT
                v.[mes_id] AS MesID,
                r.[code] AS WorkcenterCode,
                v.[starttime] AS Starttime,
                v.[endtime] AS Endtime,
                v.[duration] AS StateDurationSeconds,
                v.[state_name] AS StateName,
                v.[availability] AS Availability,
                m.[duration_utilization] AS DurationUtilizationSeconds,
                m.[performance_total] AS PerformanceTotal,
                m.[performance_good] AS PerformanceGood,
                m.[performance_bad] AS PerformanceBad,
                m.[performance_rework] AS PerformanceRework,
                COALESCE(
                    NULLIF(
                        CAST(op.[ExpPerformance] AS decimal(18, 6)),
                        0
                    ),
                    CASE
                        WHEN op.[ProcessingTime] IS NOT NULL
                         AND op.[ProcessingTime] > 0
                        THEN CAST(60.0 / op.[ProcessingTime] AS decimal(18, 6))
                        ELSE NULL
                    END,
                    NULLIF(
                        CAST(wce.[ExpPerformance] AS decimal(18, 6)),
                        0
                    )
                ) AS PlannedPerformance,
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode]
            FROM dbo.[machine_state_vector] v
            INNER JOIN dbo.[m_res_resource] r
                ON r.[id] = v.[wc_id]
            LEFT JOIN dbo.[d_mda] m
                ON m.[mes_id] = v.[mes_id]
            LEFT JOIN [{_analyticsSchema}].[FactMdaMes] mes
                ON mes.[ID] = v.[mes_id]
            LEFT JOIN [{_analyticsSchema}].[DimWorkcenterExt] wce
                ON wce.[ID] = v.[wc_id]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            WHERE r.[code] = @workcenterCode
              AND v.[starttime] < @to
              AND v.[endtime] > @from
              AND (
                    @orderCode = ''
                    OR op.[OrderCode] LIKE '%' + @orderCode + '%'
                  )
              AND (
                    @operationCode = ''
                    OR op.[OperationCode] LIKE '%' + @operationCode + '%'
                  )
              AND (
                    @productCode = ''
                    OR op.[ProductCode] LIKE '%' + @productCode + '%'
                  )
            ORDER BY
                v.[starttime],
                v.[endtime],
                v.[state_name];
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        AddParameter(
            command,
            "@from",
            from);

        AddParameter(
            command,
            "@to",
            to);

        AddParameter(
            command,
            "@workcenterCode",
            workcenterCode.Trim());

        AddParameter(
            command,
            "@orderCode",
            orderCode?.Trim()
            ?? string.Empty);

        AddParameter(
            command,
            "@operationCode",
            operationCode?.Trim()
            ?? string.Empty);

        AddParameter(
            command,
            "@productCode",
            productCode?.Trim()
            ?? string.Empty);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var rows =
            new List<Mes06ProductionGraphRecord>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var start =
                GetDateTime(
                    reader,
                    "Starttime");

            var end =
                GetDateTime(
                    reader,
                    "Endtime");

            if (!start.HasValue
                || !end.HasValue
                || end.Value <= start.Value)
            {
                continue;
            }

            rows.Add(
                new Mes06ProductionGraphRecord
                {
                    MesId =
                        GetGuid(
                            reader,
                            "MesID")
                        ?? Guid.Empty,
                    WorkcenterCode =
                        GetString(
                            reader,
                            "WorkcenterCode"),
                    Starttime =
                        start.Value,
                    Endtime =
                        end.Value,
                    StateDurationSeconds =
                        GetDecimal(
                            reader,
                            "StateDurationSeconds"),
                    StateName =
                        GetString(
                            reader,
                            "StateName"),
                    Availability =
                        reader["Availability"] is DBNull
                            ? null
                            : Convert.ToInt32(
                                reader["Availability"]),
                    DurationUtilizationSeconds =
                        GetDecimal(
                            reader,
                            "DurationUtilizationSeconds"),
                    PerformanceTotal =
                        GetDecimal(
                            reader,
                            "PerformanceTotal"),
                    PerformanceGood =
                        GetDecimal(
                            reader,
                            "PerformanceGood"),
                    PerformanceBad =
                        GetDecimal(
                            reader,
                            "PerformanceBad"),
                    PerformanceRework =
                        GetDecimal(
                            reader,
                            "PerformanceRework"),
                    PlannedPerformance =
                        GetDecimal(
                            reader,
                            "PlannedPerformance"),
                    OrderCode =
                        GetString(
                            reader,
                            "OrderCode"),
                    OperationCode =
                        GetString(
                            reader,
                            "OperationCode"),
                    ProductCode =
                        GetString(
                            reader,
                            "ProductCode")
                });
        }

        return rows;
    }

    public async Task<IReadOnlyList<MesReportingStateColor>> GetStateColorsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        var sql =
            $"""
            SELECT
                w.[Code] AS WorkcenterCode,
                s.[Name] AS StateName,
                s.[CategoryName],
                s.[Color] AS StateColor,
                s.[CategoryColor]
            FROM [{_analyticsSchema}].[DimMdaState] s
            LEFT JOIN [{_analyticsSchema}].[DimWorkcenter] w
                ON w.[ID] = s.[WorkcenterID]
            WHERE NULLIF(LTRIM(RTRIM(s.[Name])), '') IS NOT NULL
              AND (s.[IsActive] = 1 OR s.[IsActive] IS NULL)
            ORDER BY
                w.[Code],
                s.[CategoryName],
                s.[Name];
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var result =
            new List<MesReportingStateColor>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new MesReportingStateColor
                {
                    WorkcenterCode =
                        GetString(
                            reader,
                            "WorkcenterCode"),
                    StateName =
                        GetString(
                            reader,
                            "StateName"),
                    CategoryName =
                        GetString(
                            reader,
                            "CategoryName"),
                    StateColor =
                        GetString(
                            reader,
                            "StateColor"),
                    CategoryColor =
                        GetString(
                            reader,
                            "CategoryColor")
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<MesReportingShiftEvent>> GetShiftEventsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        var sql =
            $"""
            SELECT
                s.ID,
                s.Starttime,
                s.Endtime,
                s.Name
            FROM [{_analyticsSchema}].[DimShiftEvent] s
            WHERE s.Starttime < @to
              AND COALESCE(s.Endtime, DATEADD(HOUR, 8, s.Starttime)) > @from
            ORDER BY
                s.Starttime,
                s.Name;
            """;

        await using var command =
            CreateCommand(
                connection,
                sql);

        AddParameter(
            command,
            "@from",
            from);

        AddParameter(
            command,
            "@to",
            to);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var rows =
            new List<MesReportingShiftEvent>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var start =
                GetDateTime(
                    reader,
                    "Starttime");

            if (!start.HasValue)
            {
                continue;
            }

            rows.Add(
                new MesReportingShiftEvent
                {
                    Id =
                        GetGuid(
                            reader,
                            "ID"),
                    Starttime =
                        start.Value,
                    Endtime =
                        GetDateTime(
                            reader,
                            "Endtime")
                        ?? start.Value.AddHours(8),
                    Name =
                        GetString(
                            reader,
                            "Name")
                });
        }

        return rows;
    }

    public async Task<IReadOnlyList<Mes06CounterReportRecord>> GetCounterReportAsync(
        DateTime from,
        DateTime to,
        IReadOnlyList<string> workcenterCodes,
        string orderCode,
        string operationCode,
        string productCode,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            to =
                from.AddDays(1);
        }

        if (workcenterCodes.Count == 0)
        {
            return Array.Empty<Mes06CounterReportRecord>();
        }

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _commandTimeoutSeconds;

        var workcenterParameters =
            new List<string>();

        for (var index = 0;
             index < workcenterCodes.Count;
             index++)
        {
            var parameterName =
                $"@wc{index}";

            workcenterParameters.Add(
                parameterName);

            AddParameter(
                command,
                parameterName,
                workcenterCodes[index]);
        }

        command.CommandText =
            $"""
            SELECT TOP (@maxRows)
                c.[MesID],
                c.[Timestamp],
                sh.[Name] AS ShiftName,
                sh.[Starttime] AS ShiftStart,
                wc.[Code] AS WorkcenterCode,
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                op.[OrderQuantity],
                sap.SapArticleNumber,
                dc.[Name] AS CounterName,
                dc.[Description] AS CounterDescription,
                dc.[Kind] AS CounterKind,
                c.[Value],
                c.[CustomText]
            FROM [{_analyticsSchema}].[FactMdaCounter] c
            INNER JOIN [{_analyticsSchema}].[DimMdaCounter] dc
                ON dc.[ID] = c.[CounterID]
            INNER JOIN [{_analyticsSchema}].[FactMdaMes] mes
                ON mes.[ID] = c.[MesID]
            LEFT JOIN [{_analyticsSchema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_analyticsSchema}].[DimShiftEvent] sh
                ON sh.[ID] = mes.[ShiftID]
            OUTER APPLY
            (
                SELECT TOP (1)
                    NULLIF(
                        LTRIM(
                            RTRIM(
                                po.[cf_customer])),
                        '') AS SapArticleNumber
                FROM dbo.[d_pda_po] po
                WHERE po.[code] = op.[OrderCode]
                ORDER BY po.[change_id] DESC
            ) sap
            WHERE c.[Timestamp] >= @from
              AND c.[Timestamp] < @to
              AND wc.[Code] IN ({string.Join(", ", workcenterParameters)})
              AND (
                    @orderCode = ''
                    OR op.[OrderCode] LIKE '%' + @orderCode + '%'
                  )
              AND (
                    @operationCode = ''
                    OR op.[OperationCode] LIKE '%' + @operationCode + '%'
                  )
              AND (
                    @productCode = ''
                    OR op.[ProductCode] LIKE '%' + @productCode + '%'
                  )
            ORDER BY
                c.[Timestamp],
                wc.[Code],
                dc.[Kind],
                dc.[Name];
            """;

        AddParameter(
            command,
            "@from",
            from);

        AddParameter(
            command,
            "@to",
            to);

        AddParameter(
            command,
            "@orderCode",
            orderCode?.Trim()
            ?? string.Empty);

        AddParameter(
            command,
            "@operationCode",
            operationCode?.Trim()
            ?? string.Empty);

        AddParameter(
            command,
            "@productCode",
            productCode?.Trim()
            ?? string.Empty);

        AddParameter(
            command,
            "@maxRows",
            Math.Clamp(
                maxRows,
                1,
                10000));

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var rows =
            new List<Mes06CounterReportRecord>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var timestamp =
                GetDateTime(
                    reader,
                    "Timestamp");

            if (!timestamp.HasValue)
            {
                continue;
            }

            rows.Add(
                new Mes06CounterReportRecord
                {
                    MesId =
                        GetGuid(
                            reader,
                            "MesID")
                        ?? Guid.Empty,
                    Timestamp =
                        timestamp.Value,
                    Starttime =
                        timestamp.Value,
                    ShiftName =
                        GetString(
                            reader,
                            "ShiftName"),
                    ShiftStart =
                        GetDateTime(
                            reader,
                            "ShiftStart"),
                    WorkcenterCode =
                        GetString(
                            reader,
                            "WorkcenterCode"),
                    OrderCode =
                        GetString(
                            reader,
                            "OrderCode"),
                    OperationCode =
                        GetString(
                            reader,
                            "OperationCode"),
                    ProductCode =
                        GetString(
                            reader,
                            "ProductCode"),
                    OrderQuantity =
                        GetDecimal(
                            reader,
                            "OrderQuantity"),
                    SapArticleNumber =
                        GetString(
                            reader,
                            "SapArticleNumber"),
                    CounterName =
                        GetString(
                            reader,
                            "CounterName"),
                    CounterDescription =
                        GetString(
                            reader,
                            "CounterDescription"),
                    CounterKind =
                        GetString(
                            reader,
                            "CounterKind"),
                    Value =
                        GetDecimal(
                            reader,
                            "Value"),
                    CustomText =
                        GetString(
                            reader,
                            "CustomText")
                });
        }

        await AttachWorkersAsync(
            rows,
            cancellationToken);

        return rows;
    }

    private async Task AttachWorkersAsync(
        IReadOnlyList<Mes06CounterReportRecord> rows,
        CancellationToken cancellationToken)
    {
        var mesIds =
            rows
                .Where(row =>
                    row.MesId != Guid.Empty)
                .Select(row =>
                    row.MesId)
                .Distinct()
                .ToList();

        if (mesIds.Count == 0)
        {
            return;
        }

        var links =
            await LoadWorkerLinksAsync(
                mesIds,
                cancellationToken);

        if (links.Count == 0)
        {
            return;
        }

        var explicitMainWorkers =
            await LoadExplicitMainWorkerKeysAsync(
                mesIds,
                cancellationToken);

        var linksByMes =
            links
                .GroupBy(link =>
                    link.MesId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList());

        foreach (var row
                 in rows)
        {
            if (!linksByMes.TryGetValue(
                    row.MesId,
                    out var candidates))
            {
                continue;
            }

            var active =
                candidates
                    .Where(link =>
                        IsWorkerActiveAt(
                            link,
                            row.Timestamp))
                    .GroupBy(
                        link =>
                            BuildWorkerIdentityKey(
                                link.HumanCode,
                                link.FirstName,
                                link.LastName),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                        group
                            .OrderByDescending(link =>
                                link.Starttime
                                ?? DateTime.MinValue)
                            .First())
                    .Select(link =>
                    {
                        var identityKey =
                            BuildMainWorkerKey(
                                row.MesId,
                                link.HumanCode);

                        return new Mes06CounterWorkerRecord
                        {
                            HumanCode =
                                link.HumanCode,
                            FirstName =
                                link.FirstName,
                            LastName =
                                link.LastName,
                            IsMainWorker =
                                explicitMainWorkers.Contains(
                                    identityKey)
                        };
                    })
                    .OrderByDescending(worker =>
                        worker.IsMainWorker)
                    .ThenBy(worker =>
                        worker.LastName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(worker =>
                        worker.FirstName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(worker =>
                        worker.HumanCode,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            for (var index = 0;
                 index < active.Count;
                 index++)
            {
                active[index].Separator =
                    index < active.Count - 1
                        ? "; "
                        : string.Empty;
            }

            row.Workers =
                active;
        }
    }

    private async Task<IReadOnlyList<Mes06WorkerLinkRow>> LoadWorkerLinksAsync(
        IReadOnlyList<Guid> mesIds,
        CancellationToken cancellationToken)
    {
        var result =
            new List<Mes06WorkerLinkRow>();

        const int batchSize = 400;

        for (var offset = 0;
             offset < mesIds.Count;
             offset += batchSize)
        {
            var batch =
                mesIds
                    .Skip(offset)
                    .Take(batchSize)
                    .ToList();

            await using var connection =
                CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandTimeout =
                _commandTimeoutSeconds;

            var parameters =
                new List<string>();

            for (var index = 0;
                 index < batch.Count;
                 index++)
            {
                var parameterName =
                    $"@mes{index}";

                parameters.Add(
                    parameterName);

                AddParameter(
                    command,
                    parameterName,
                    batch[index]);
            }

            command.CommandText =
                $"""
                SELECT
                    h.[MesID],
                    h.[HumanCode],
                    h.[FirstName],
                    h.[LastName],
                    h.[Starttime],
                    h.[Endtime],
                    h.[Duration],
                    h.[Amount]
                FROM [{_analyticsSchema}].[FactHResLinkExt] h
                WHERE h.[MesID] IN ({string.Join(", ", parameters)})
                  AND (
                        NULLIF(LTRIM(RTRIM(h.[HumanCode])), '') IS NOT NULL
                        OR NULLIF(LTRIM(RTRIM(h.[FirstName])), '') IS NOT NULL
                        OR NULLIF(LTRIM(RTRIM(h.[LastName])), '') IS NOT NULL
                      )
                ORDER BY
                    h.[MesID],
                    h.[Starttime],
                    h.[LastName],
                    h.[FirstName];
                """;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                var mesId =
                    GetGuid(
                        reader,
                        "MesID");

                if (!mesId.HasValue)
                {
                    continue;
                }

                result.Add(
                    new Mes06WorkerLinkRow
                    {
                        MesId =
                            mesId.Value,
                        HumanCode =
                            GetString(
                                reader,
                                "HumanCode"),
                        FirstName =
                            GetString(
                                reader,
                                "FirstName"),
                        LastName =
                            GetString(
                                reader,
                                "LastName"),
                        Starttime =
                            GetDateTime(
                                reader,
                                "Starttime"),
                        Endtime =
                            GetDateTime(
                                reader,
                                "Endtime"),
                        Amount =
                            GetDecimal(
                                reader,
                                "Amount")
                    });
            }
        }

        return result;
    }

    private async Task<HashSet<string>> LoadExplicitMainWorkerKeysAsync(
        IReadOnlyList<Guid> mesIds,
        CancellationToken cancellationToken)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        // FactHResLinkExt itself does NOT expose a main-worker flag in the
        // known analytical schema. Do not guess from Amount/Duration.
        // Instead probe only tables that explicitly contain:
        //   a MES-event identifier + a human/person code + a main/primary flag.
        IReadOnlyList<Mes06MetadataTable> tables;

        try
        {
            tables =
                await LoadMetadataTablesAsync(
                    cancellationToken);
        }
        catch
        {
            return result;
        }

        var candidates =
            tables
                .Select(table =>
                    new
                    {
                        Table = table,
                        MesColumn =
                            FindColumn(
                                table.Columns,
                                "MesID",
                                "MESID",
                                "MdaMesID",
                                "MdaID",
                                "mes_id",
                                "mda_mes_id",
                                "mda_id"),
                        HumanColumn =
                            FindColumn(
                                table.Columns,
                                "HumanCode",
                                "PersonnelNumber",
                                "PersonnelNo",
                                "PersonCode",
                                "EmployeeCode",
                                "WorkerCode",
                                "human_code",
                                "personnel_number",
                                "personnel_no",
                                "person_code",
                                "employee_code",
                                "worker_code"),
                        MainColumn =
                            FindExplicitMainWorkerColumn(
                                table.Columns)
                    })
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(
                        candidate.MesColumn)
                    && !string.IsNullOrWhiteSpace(
                        candidate.HumanColumn)
                    && !string.IsNullOrWhiteSpace(
                        candidate.MainColumn))
                .OrderByDescending(candidate =>
                    MainWorkerMetadataScore(
                        candidate.Table))
                .Take(12)
                .ToList();

        foreach (var candidate
                 in candidates)
        {
            try
            {
                var loaded =
                    await ReadMainWorkerCandidateAsync(
                        candidate.Table,
                        candidate.MesColumn!,
                        candidate.HumanColumn!,
                        candidate.MainColumn!,
                        mesIds,
                        result,
                        cancellationToken);

                if (loaded > 0)
                {
                    // Prefer the first explicit candidate that actually marks
                    // one or more workers as main/primary.
                    return result;
                }
            }
            catch
            {
                // Candidate schema may be unrelated or access-limited.
                // Continue with the next explicit candidate.
            }
        }

        return result;
    }

    private async Task<int> ReadMainWorkerCandidateAsync(
        Mes06MetadataTable table,
        string mesColumn,
        string humanColumn,
        string mainColumn,
        IReadOnlyList<Guid> mesIds,
        HashSet<string> result,
        CancellationToken cancellationToken)
    {
        var loaded = 0;
        const int batchSize = 300;

        for (var offset = 0;
             offset < mesIds.Count;
             offset += batchSize)
        {
            var batch =
                mesIds
                    .Skip(offset)
                    .Take(batchSize)
                    .ToList();

            await using var connection =
                CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandTimeout =
                Math.Min(
                    _commandTimeoutSeconds,
                    8);

            var parameters =
                new List<string>();

            for (var index = 0;
                 index < batch.Count;
                 index++)
            {
                var parameterName =
                    $"@mainMes{index}";

                parameters.Add(
                    parameterName);

                AddParameter(
                    command,
                    parameterName,
                    batch[index]);
            }

            command.CommandText =
                $"""
                SELECT
                    CONVERT(nvarchar(64), {QuoteIdentifier(mesColumn)}) AS MesID,
                    CONVERT(nvarchar(255), {QuoteIdentifier(humanColumn)}) AS HumanCode,
                    CONVERT(nvarchar(64), {QuoteIdentifier(mainColumn)}) AS MainValue
                FROM {SqlObjectName(table)}
                WHERE {QuoteIdentifier(mesColumn)} IN ({string.Join(", ", parameters)});
                """;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                var mesText =
                    GetString(
                        reader,
                        "MesID");

                var humanCode =
                    GetString(
                        reader,
                        "HumanCode")
                    .Trim();

                var mainValue =
                    GetString(
                        reader,
                        "MainValue");

                if (!Guid.TryParse(
                        mesText,
                        out var mesId)
                    || string.IsNullOrWhiteSpace(
                        humanCode)
                    || !IsTruthyMainWorkerValue(
                        mainValue))
                {
                    continue;
                }

                if (result.Add(
                        BuildMainWorkerKey(
                            mesId,
                            humanCode)))
                {
                    loaded++;
                }
            }
        }

        return loaded;
    }

    private async Task<IReadOnlyList<Mes06MetadataTable>> LoadMetadataTablesAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY
                TABLE_SCHEMA,
                TABLE_NAME,
                ORDINAL_POSITION;
            """;

        var columns =
            new List<Mes06MetadataColumnRow>();

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            CreateCommand(
                connection,
                sql);

        command.CommandTimeout =
            Math.Min(
                command.CommandTimeout,
                8);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            columns.Add(
                new Mes06MetadataColumnRow
                {
                    Schema =
                        GetString(
                            reader,
                            "TABLE_SCHEMA"),
                    Table =
                        GetString(
                            reader,
                            "TABLE_NAME"),
                    Column =
                        GetString(
                            reader,
                            "COLUMN_NAME")
                });
        }

        return columns
            .GroupBy(row =>
                (row.Schema, row.Table))
            .Select(group =>
                new Mes06MetadataTable
                {
                    Schema =
                        group.Key.Schema,
                    Table =
                        group.Key.Table,
                    Columns =
                        group
                            .Select(row =>
                                row.Column)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList()
                })
            .ToList();
    }

    private static string? FindExplicitMainWorkerColumn(
        IReadOnlyList<string> columns)
    {
        var exact =
            FindColumn(
                columns,
                "IsMainWorker",
                "MainWorker",
                "MainWorkerFlag",
                "IsPrimaryWorker",
                "PrimaryWorker",
                "IsMainHumanResource",
                "MainHumanResource",
                "IsPrimaryHumanResource",
                "PrimaryHumanResource",
                "is_main_worker",
                "main_worker",
                "main_worker_flag",
                "is_primary_worker",
                "primary_worker",
                "is_main_human_resource",
                "main_human_resource");

        if (!string.IsNullOrWhiteSpace(
                exact))
        {
            return exact;
        }

        return columns.FirstOrDefault(column =>
        {
            var normalized =
                column
                    .Replace("_", string.Empty)
                    .Replace("-", string.Empty);

            return (
                       normalized.Contains(
                           "main",
                           StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(
                           "primary",
                           StringComparison.OrdinalIgnoreCase)
                   )
                   && (
                       normalized.Contains(
                           "worker",
                           StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(
                           "human",
                           StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(
                           "hres",
                           StringComparison.OrdinalIgnoreCase)
                   );
        });
    }

    private static int MainWorkerMetadataScore(
        Mes06MetadataTable table)
    {
        var score = 0;

        if (table.Table.Contains(
                "hres",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "human",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (table.Table.Contains(
                "worker",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "person",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        if (table.Table.Contains(
                "link",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "assign",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (table.Schema.Equals(
                "ana",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }

    private static bool IsTruthyMainWorkerValue(
        string value)
    {
        var normalized =
            value.Trim();

        return normalized.Equals(
                   "1",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(
                   "true",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(
                   "yes",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(
                   "y",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(
                   "x",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(
                   "main",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(
                   "primary",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorkerActiveAt(
        Mes06WorkerLinkRow link,
        DateTime timestamp)
    {
        if (link.Starttime.HasValue
            && timestamp < link.Starttime.Value)
        {
            return false;
        }

        if (link.Endtime.HasValue
            && timestamp >= link.Endtime.Value)
        {
            return false;
        }

        return true;
    }

    private static string BuildWorkerIdentityKey(
        string humanCode,
        string firstName,
        string lastName)
    {
        if (!string.IsNullOrWhiteSpace(
                humanCode))
        {
            return humanCode.Trim();
        }

        return $"{lastName.Trim()}|{firstName.Trim()}";
    }

    private static string BuildMainWorkerKey(
        Guid mesId,
        string humanCode) =>
        $"{mesId:D}|{humanCode.Trim()}";

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetWorkcenterGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        const string metadataSql = """
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;
            """;

        var columns =
            new List<Mes06MetadataColumnRow>();

        await using (var connection =
                     CreateConnection())
        {
            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                CreateCommand(
                    connection,
                    metadataSql);

            command.CommandTimeout =
                Math.Min(
                    command.CommandTimeout,
                    8);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                columns.Add(
                    new Mes06MetadataColumnRow
                    {
                        Schema =
                            GetString(
                                reader,
                                "TABLE_SCHEMA"),
                        Table =
                            GetString(
                                reader,
                                "TABLE_NAME"),
                        Column =
                            GetString(
                                reader,
                                "COLUMN_NAME")
                    });
            }
        }

        var tables =
            columns
                .GroupBy(row =>
                    (row.Schema, row.Table))
                .Select(group =>
                    new Mes06MetadataTable
                    {
                        Schema =
                            group.Key.Schema,
                        Table =
                            group.Key.Table,
                        Columns =
                            group
                                .Select(row =>
                                    row.Column)
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)
                                .ToList()
                    })
                .ToList();

        var mappings =
            new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);

        await LoadDirectWorkcenterGroupsAsync(
            tables,
            mappings,
            cancellationToken);

        if (mappings.Count == 0)
        {
            await LoadRelationalWorkcenterGroupsAsync(
                tables,
                mappings,
                cancellationToken);
        }

        return mappings.ToDictionary(
            pair => pair.Key,
            pair =>
                (IReadOnlyList<string>)pair.Value
                    .OrderBy(
                        value => value,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task LoadDirectWorkcenterGroupsAsync(
        IReadOnlyList<Mes06MetadataTable> tables,
        Dictionary<string, HashSet<string>> mappings,
        CancellationToken cancellationToken)
    {
        var candidates =
            tables
                .Select(table =>
                    new
                    {
                        Table =
                            table,
                        CodeColumn =
                            FindColumn(
                                table.Columns,
                                "WorkcenterCode",
                                "WorkCenterCode",
                                "WorkCentreCode",
                                "ResourceCode",
                                "RessourceCode",
                                "Code"),
                        GroupColumn =
                            FindColumn(
                                table.Columns,
                                "Groups",
                                "WorkcenterGroups",
                                "WorkCenterGroups",
                                "GroupNames",
                                "GroupName",
                                "Group")
                    })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.CodeColumn)
                    && !string.IsNullOrWhiteSpace(
                        item.GroupColumn)
                    && WorkcenterMetadataScore(
                        item.Table) > 0)
                .OrderByDescending(item =>
                    WorkcenterMetadataScore(
                        item.Table))
                .Take(10)
                .ToList();

        foreach (var candidate
                 in candidates)
        {
            try
            {
                var sql =
                    $"""
                    SELECT TOP (10000)
                        CONVERT(nvarchar(255), {QuoteIdentifier(candidate.CodeColumn!)}) AS WorkcenterCode,
                        CONVERT(nvarchar(1000), {QuoteIdentifier(candidate.GroupColumn!)}) AS WorkcenterGroup
                    FROM {SqlObjectName(candidate.Table)}
                    WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(255), {QuoteIdentifier(candidate.CodeColumn!)}))), '') IS NOT NULL;
                    """;

                var loaded =
                    await ReadWorkcenterGroupPairsAsync(
                        sql,
                        mappings,
                        cancellationToken);

                if (loaded > 0)
                {
                    return;
                }
            }
            catch
            {
                // Probe the next readable configuration object.
            }
        }
    }

    private async Task LoadRelationalWorkcenterGroupsAsync(
        IReadOnlyList<Mes06MetadataTable> tables,
        Dictionary<string, HashSet<string>> mappings,
        CancellationToken cancellationToken)
    {
        var bridges =
            tables
                .Select(table =>
                    new
                    {
                        Table = table,
                        WorkcenterId =
                            FindWorkcenterIdColumn(
                                table.Columns),
                        WorkcenterCode =
                            FindWorkcenterCodeColumn(
                                table.Columns),
                        GroupId =
                            FindGroupIdColumn(
                                table.Columns),
                        GroupName =
                            FindDirectGroupNameColumn(
                                table.Columns)
                    })
                .Where(item =>
                    (!string.IsNullOrWhiteSpace(
                         item.WorkcenterId)
                     || !string.IsNullOrWhiteSpace(
                         item.WorkcenterCode))
                    && (!string.IsNullOrWhiteSpace(
                            item.GroupId)
                        || !string.IsNullOrWhiteSpace(
                            item.GroupName))
                    && BridgeMetadataScore(
                        item.Table) > 0)
                .OrderByDescending(item =>
                    BridgeMetadataScore(
                        item.Table))
                .Take(12)
                .ToList();

        var workcenterTables =
            tables
                .Select(table =>
                    new
                    {
                        Table = table,
                        Id =
                            FindEntityIdColumn(
                                table.Columns,
                                "workcenter",
                                "resource",
                                "ressource"),
                        Code =
                            FindColumn(
                                table.Columns,
                                "WorkcenterCode",
                                "WorkCenterCode",
                                "WorkCentreCode",
                                "ResourceCode",
                                "RessourceCode",
                                "Code")
                    })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Id)
                    && !string.IsNullOrWhiteSpace(
                        item.Code)
                    && WorkcenterMetadataScore(
                        item.Table) > 0)
                .OrderByDescending(item =>
                    WorkcenterMetadataScore(
                        item.Table))
                .Take(10)
                .ToList();

        var groupTables =
            tables
                .Select(table =>
                    new
                    {
                        Table = table,
                        Id =
                            FindEntityIdColumn(
                                table.Columns,
                                "group"),
                        Display =
                            FindColumn(
                                table.Columns,
                                "GroupCode",
                                "group_code",
                                "Code",
                                "Name",
                                "GroupName",
                                "group_name",
                                "Description",
                                "Designation")
                    })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Id)
                    && !string.IsNullOrWhiteSpace(
                        item.Display)
                    && GroupMetadataScore(
                        item.Table) > 0)
                .OrderByDescending(item =>
                    GroupMetadataScore(
                        item.Table))
                .Take(10)
                .ToList();

        foreach (var bridge
                 in bridges)
        {
            if (!string.IsNullOrWhiteSpace(
                    bridge.WorkcenterCode)
                && !string.IsNullOrWhiteSpace(
                    bridge.GroupName))
            {
                var sql =
                    $"""
                    SELECT TOP (10000)
                        CONVERT(nvarchar(255), b.{QuoteIdentifier(bridge.WorkcenterCode!)}) AS WorkcenterCode,
                        CONVERT(nvarchar(1000), b.{QuoteIdentifier(bridge.GroupName!)}) AS WorkcenterGroup
                    FROM {SqlObjectName(bridge.Table)} b;
                    """;

                if (await TryReadWorkcenterGroupPairsAsync(
                        sql,
                        mappings,
                        cancellationToken))
                {
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    bridge.WorkcenterId)
                && !string.IsNullOrWhiteSpace(
                    bridge.GroupName))
            {
                foreach (var wc
                         in RankBySchema(
                             workcenterTables,
                             bridge.Table.Schema)
                             .Take(6))
                {
                    var sql =
                        $"""
                        SELECT TOP (10000)
                            CONVERT(nvarchar(255), wc.{QuoteIdentifier(wc.Code!)}) AS WorkcenterCode,
                            CONVERT(nvarchar(1000), b.{QuoteIdentifier(bridge.GroupName!)}) AS WorkcenterGroup
                        FROM {SqlObjectName(bridge.Table)} b
                        INNER JOIN {SqlObjectName(wc.Table)} wc
                            ON wc.{QuoteIdentifier(wc.Id!)} = b.{QuoteIdentifier(bridge.WorkcenterId!)};
                        """;

                    if (await TryReadWorkcenterGroupPairsAsync(
                            sql,
                            mappings,
                            cancellationToken))
                    {
                        return;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    bridge.WorkcenterId)
                && !string.IsNullOrWhiteSpace(
                    bridge.GroupId))
            {
                foreach (var wc
                         in RankBySchema(
                             workcenterTables,
                             bridge.Table.Schema)
                             .Take(6))
                {
                    foreach (var group
                             in RankBySchema(
                                 groupTables,
                                 bridge.Table.Schema)
                                 .Take(8))
                    {
                        var sql =
                            $"""
                            SELECT TOP (10000)
                                CONVERT(nvarchar(255), wc.{QuoteIdentifier(wc.Code!)}) AS WorkcenterCode,
                                CONVERT(nvarchar(1000), g.{QuoteIdentifier(group.Display!)}) AS WorkcenterGroup
                            FROM {SqlObjectName(bridge.Table)} b
                            INNER JOIN {SqlObjectName(wc.Table)} wc
                                ON wc.{QuoteIdentifier(wc.Id!)} = b.{QuoteIdentifier(bridge.WorkcenterId!)}
                            INNER JOIN {SqlObjectName(group.Table)} g
                                ON g.{QuoteIdentifier(group.Id!)} = b.{QuoteIdentifier(bridge.GroupId!)};
                            """;

                        if (await TryReadWorkcenterGroupPairsAsync(
                                sql,
                                mappings,
                                cancellationToken))
                        {
                            return;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    bridge.WorkcenterCode)
                && !string.IsNullOrWhiteSpace(
                    bridge.GroupId))
            {
                foreach (var group
                         in RankBySchema(
                             groupTables,
                             bridge.Table.Schema)
                             .Take(8))
                {
                    var sql =
                        $"""
                        SELECT TOP (10000)
                            CONVERT(nvarchar(255), b.{QuoteIdentifier(bridge.WorkcenterCode!)}) AS WorkcenterCode,
                            CONVERT(nvarchar(1000), g.{QuoteIdentifier(group.Display!)}) AS WorkcenterGroup
                        FROM {SqlObjectName(bridge.Table)} b
                        INNER JOIN {SqlObjectName(group.Table)} g
                            ON g.{QuoteIdentifier(group.Id!)} = b.{QuoteIdentifier(bridge.GroupId!)};
                        """;

                    if (await TryReadWorkcenterGroupPairsAsync(
                            sql,
                            mappings,
                            cancellationToken))
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task<bool> TryReadWorkcenterGroupPairsAsync(
        string sql,
        Dictionary<string, HashSet<string>> mappings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadWorkcenterGroupPairsAsync(
                       sql,
                       mappings,
                       cancellationToken) > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> ReadWorkcenterGroupPairsAsync(
        string sql,
        Dictionary<string, HashSet<string>> mappings,
        CancellationToken cancellationToken)
    {
        var loaded = 0;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            CreateCommand(
                connection,
                sql);

        command.CommandTimeout =
            Math.Min(
                command.CommandTimeout,
                8);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var code =
                GetString(
                    reader,
                    "WorkcenterCode")
                .Trim();

            var rawGroup =
                GetString(
                    reader,
                    "WorkcenterGroup")
                .Trim();

            if (string.IsNullOrWhiteSpace(
                    code)
                || string.IsNullOrWhiteSpace(
                    rawGroup))
            {
                continue;
            }

            if (!mappings.TryGetValue(
                    code,
                    out var groups))
            {
                groups =
                    new HashSet<string>(
                        StringComparer.CurrentCultureIgnoreCase);

                mappings[code] =
                    groups;
            }

            foreach (var groupName
                     in ParseGroups(
                         rawGroup))
            {
                if (groups.Add(
                        groupName))
                {
                    loaded++;
                }
            }
        }

        return loaded;
    }

    private static int WorkcenterMetadataScore(
        Mes06MetadataTable table)
    {
        var score = 0;
        var name = table.Table;

        if (name.Contains(
                "workcenter",
                StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                "workcentre",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (name.Contains(
                "resource",
                StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                "ressource",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 70;
        }

        if (string.Equals(
                name,
                "m_res",
                StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(
                "m_res_",
                StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                "_res_",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        if (table.Columns.Any(column =>
                column.Contains(
                    "workcenter",
                    StringComparison.OrdinalIgnoreCase)
                || column.Contains(
                    "resource",
                    StringComparison.OrdinalIgnoreCase)))
        {
            score += 40;
        }

        return score;
    }

    private static int GroupMetadataScore(
        Mes06MetadataTable table)
    {
        var score = 0;

        if (table.Table.Contains(
                "group",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (string.Equals(
                table.Table,
                "m_res_group",
                StringComparison.OrdinalIgnoreCase))
        {
            // User-verified FASTEC group master table.
            score += 500;
        }

        if (table.Columns.Any(column =>
                column.Contains(
                    "group",
                    StringComparison.OrdinalIgnoreCase)))
        {
            score += 40;
        }

        return score;
    }

    private static int BridgeMetadataScore(
        Mes06MetadataTable table)
    {
        var score =
            GroupMetadataScore(
                table);

        if (table.Table.Contains(
                "map",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "link",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "member",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "assign",
                StringComparison.OrdinalIgnoreCase)
            || table.Table.Contains(
                "relation",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (table.Table.StartsWith(
                "m_res_",
                StringComparison.OrdinalIgnoreCase)
            && table.Table.Contains(
                "group",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                table.Table,
                "m_res_group",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }

        return score;
    }

    private static string? FindWorkcenterIdColumn(
        IReadOnlyList<string> columns) =>
        FindColumn(
            columns,
            "WorkcenterID",
            "WorkCenterID",
            "WorkCentreID",
            "ResourceID",
            "RessourceID",
            "MachineID",
            "workcenter_id",
            "work_center_id",
            "resource_id",
            "ressource_id",
            "res_id",
            "machine_id",
            "m_res_id")
        ?? columns.FirstOrDefault(column =>
            column.EndsWith(
                "ID",
                StringComparison.OrdinalIgnoreCase)
            && (column.Contains(
                    "workcenter",
                    StringComparison.OrdinalIgnoreCase)
                || column.Contains(
                    "resource",
                    StringComparison.OrdinalIgnoreCase)
                || column.Contains(
                    "ressource",
                    StringComparison.OrdinalIgnoreCase)
                || column.Contains(
                    "res_",
                    StringComparison.OrdinalIgnoreCase))
            && !column.Contains(
                "group",
                StringComparison.OrdinalIgnoreCase));

    private static string? FindWorkcenterCodeColumn(
        IReadOnlyList<string> columns) =>
        FindColumn(
            columns,
            "WorkcenterCode",
            "WorkCenterCode",
            "WorkCentreCode",
            "ResourceCode",
            "RessourceCode",
            "workcenter_code",
            "work_center_code",
            "resource_code",
            "ressource_code",
            "res_code");

    private static string? FindGroupIdColumn(
        IReadOnlyList<string> columns) =>
        FindColumn(
            columns,
            "WorkcenterGroupID",
            "WorkCenterGroupID",
            "WorkCentreGroupID",
            "ResourceGroupID",
            "RessourceGroupID",
            "GroupID",
            "workcenter_group_id",
            "work_center_group_id",
            "resource_group_id",
            "ressource_group_id",
            "res_group_id",
            "group_id",
            "m_res_group_id")
        ?? columns.FirstOrDefault(column =>
            column.EndsWith(
                "ID",
                StringComparison.OrdinalIgnoreCase)
            && column.Contains(
                "group",
                StringComparison.OrdinalIgnoreCase));

    private static string? FindDirectGroupNameColumn(
        IReadOnlyList<string> columns) =>
        FindColumn(
            columns,
            "Groups",
            "WorkcenterGroups",
            "WorkCenterGroups",
            "GroupNames",
            "GroupName",
            "Group",
            "group_code",
            "group_name",
            "groups",
            "group");

    private static string? FindEntityIdColumn(
        IReadOnlyList<string> columns,
        params string[] entityTokens)
    {
        var exact =
            FindColumn(
                columns,
                "ID");

        if (!string.IsNullOrWhiteSpace(
                exact))
        {
            return exact;
        }

        foreach (var token
                 in entityTokens)
        {
            var match =
                columns.FirstOrDefault(column =>
                    string.Equals(
                        column,
                        token + "ID",
                        StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(
                    match))
            {
                return match;
            }
        }

        return columns.FirstOrDefault(column =>
            column.EndsWith(
                "ID",
                StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<T> RankBySchema<T>(
        IEnumerable<T> candidates,
        string preferredSchema)
        where T : class
    {
        return candidates.OrderByDescending(candidate =>
        {
            var tableProperty =
                candidate
                    .GetType()
                    .GetProperty(
                        "Table");

            if (tableProperty?.GetValue(
                    candidate)
                is Mes06MetadataTable table)
            {
                return string.Equals(
                    table.Schema,
                    preferredSchema,
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
            }

            return 0;
        });
    }

    private static string SqlObjectName(
        Mes06MetadataTable table) =>
        $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Table)}";

    private static string QuoteIdentifier(
        string identifier) =>
        $"[{identifier.Replace("]", "]]")}]";

    private static string? FindColumn(
        IEnumerable<string> available,
        params string[] candidates)
    {
        foreach (var candidate
                 in candidates)
        {
            var match =
                available.FirstOrDefault(column =>
                    string.Equals(
                        column,
                        candidate,
                        StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(
                    match))
            {
                return match;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseGroups(
        string raw)
    {
        if (string.IsNullOrWhiteSpace(
                raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(
                new[] { ',', ';', '|' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value =>
                value.Trim())
            .Where(value =>
                !string.IsNullOrWhiteSpace(
                    value))
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSapNumbersByOrderAsync(
        IEnumerable<string> orderCodes,
        CancellationToken cancellationToken = default)
    {
        var distinct =
            orderCodes
                .Where(code =>
                    !string.IsNullOrWhiteSpace(
                        code))
                .Select(code =>
                    code.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (distinct.Count == 0)
        {
            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }

        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        const int batchSize = 500;

        for (var offset = 0;
             offset < distinct.Count;
             offset += batchSize)
        {
            var batch =
                distinct
                    .Skip(offset)
                    .Take(batchSize)
                    .ToList();

            var parameterNames =
                batch
                    .Select((_, index) =>
                        $"@order{index}")
                    .ToArray();

            var sql =
                $"""
                SELECT
                    po.code,
                    MAX(
                        NULLIF(
                            LTRIM(
                                RTRIM(
                                    po.cf_customer)),
                            '')) AS SapArticleNumber
                FROM dbo.d_pda_po po
                WHERE po.code IN ({string.Join(", ", parameterNames)})
                GROUP BY po.code;
                """;

            await using var command =
                CreateCommand(
                    connection,
                    sql);

            for (var index = 0;
                 index < batch.Count;
                 index++)
            {
                AddParameter(
                    command,
                    parameterNames[index],
                    batch[index]);
            }

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                var orderCode =
                    GetString(
                        reader,
                        "code");

                var sapNumber =
                    GetString(
                        reader,
                        "SapArticleNumber");

                if (!string.IsNullOrWhiteSpace(
                        orderCode)
                    && !string.IsNullOrWhiteSpace(
                        sapNumber))
                {
                    result[orderCode] =
                        sapNumber;
                }
            }
        }

        return result;
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

        if (string.IsNullOrWhiteSpace(
                server))
        {
            throw new InvalidOperationException(
                "MESSET does not contain a SQL server address.");
        }

        if (string.IsNullOrWhiteSpace(
                database))
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
            $"Server={server};Database={database};Integrated Security=True;Encrypt={encrypt};TrustServerCertificate={trust};Connect Timeout={timeout};Application Name=DMS MES06 Enrichment;";

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

        command.CommandText =
            sql;

        command.CommandType =
            CommandType.Text;

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

        parameter.ParameterName =
            name;

        parameter.Value =
            value
            ?? DBNull.Value;

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
                    _settings) is object value)
            {
                var text =
                    Convert.ToString(
                        value);

                if (!string.IsNullOrWhiteSpace(
                        text))
                {
                    return text.Trim();
                }
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

            var raw =
                property?.GetValue(
                    _settings);

            if (raw is not null
                && int.TryParse(
                    Convert.ToString(
                        raw),
                    out var value))
            {
                return value;
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

            var raw =
                property?.GetValue(
                    _settings);

            if (raw is bool value)
            {
                return value;
            }

            if (raw is not null
                && bool.TryParse(
                    Convert.ToString(
                        raw),
                    out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static string GetString(
        DbDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(
                ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(
                      ordinal))
              ?? string.Empty;
    }

    private static Guid? GetGuid(
        DbDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }

        var value =
            reader.GetValue(
                ordinal);

        if (value is Guid guid)
        {
            return guid;
        }

        return Guid.TryParse(
            Convert.ToString(
                value),
            out var parsed)
            ? parsed
            : null;
    }

    private static decimal? GetDecimal(
        DbDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }

        try
        {
            return Convert.ToDecimal(
                reader.GetValue(
                    ordinal));
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? GetDateTime(
        DbDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }

        var value =
            reader.GetValue(
                ordinal);

        return value switch
        {
            DateTime dateTime =>
                dateTime,

            DateTimeOffset offset =>
                offset.DateTime,

            _ =>
                DateTime.TryParse(
                    Convert.ToString(
                        value),
                    out var parsed)
                    ? parsed
                    : null
        };
    }
}

public sealed class Mes06CounterReportRecord
{
    public Guid MesId { get; init; }
    public DateTime Timestamp { get; init; }

    // Alias used by the existing MES06 shift resolver.
    public DateTime Starttime { get; init; }

    public string ShiftName { get; init; } = string.Empty;
    public DateTime? ShiftStart { get; init; }

    public string WorkcenterCode { get; init; } = string.Empty;
    public string OrderCode { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public decimal? OrderQuantity { get; init; }
    public string SapArticleNumber { get; init; } = string.Empty;

    public string CounterName { get; init; } = string.Empty;
    public string CounterDescription { get; init; } = string.Empty;
    public string CounterKind { get; init; } = string.Empty;
    public decimal? Value { get; init; }
    public string CustomText { get; init; } = string.Empty;

    public IReadOnlyList<Mes06CounterWorkerRecord> Workers { get; set; } =
        Array.Empty<Mes06CounterWorkerRecord>();

    public string WorkersDisplay =>
        string.Join(
            "; ",
            Workers.Select(worker =>
                worker.DisplayText));
}

public sealed class Mes06CounterWorkerRecord
{
    public string HumanCode { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool IsMainWorker { get; init; }

    // Used by the WPF item template to append semicolons between operators.
    public string Separator { get; set; } = string.Empty;

    public string DisplayText
    {
        get
        {
            var name =
                !string.IsNullOrWhiteSpace(
                    LastName)
                    && !string.IsNullOrWhiteSpace(
                        FirstName)
                    ? $"{LastName}, {FirstName}"
                    : !string.IsNullOrWhiteSpace(
                        LastName)
                        ? LastName
                        : FirstName;

            if (!string.IsNullOrWhiteSpace(
                    HumanCode))
            {
                return string.IsNullOrWhiteSpace(
                           name)
                    ? $"[{HumanCode}]"
                    : $"{name} [{HumanCode}]";
            }

            return name;
        }
    }
}

internal sealed class Mes06WorkerLinkRow
{
    public Guid MesId { get; init; }
    public string HumanCode { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public DateTime? Starttime { get; init; }
    public DateTime? Endtime { get; init; }
    public decimal? Amount { get; init; }
}

internal sealed class Mes06MetadataColumnRow
{
    public string Schema { get; init; } = string.Empty;
    public string Table { get; init; } = string.Empty;
    public string Column { get; init; } = string.Empty;
}

internal sealed class Mes06MetadataTable
{
    public string Schema { get; init; } = string.Empty;
    public string Table { get; init; } = string.Empty;
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
}

internal sealed class Mes06OeeMesPeriod
{
    public Guid MesId { get; init; }
    public string WorkcenterCode { get; init; } = string.Empty;
    public string ShiftName { get; init; } = string.Empty;
    public DateTime? ShiftStarttime { get; init; }
    public DateTime Starttime { get; init; }
    public DateTime Endtime { get; init; }
    public double FailureSeconds { get; init; }
    public double AvailableSeconds { get; init; }
    public double PlannedShutdownSeconds { get; init; }
    public double TotalAmount { get; init; }
    public double GoodUnits { get; init; }
    public double BadUnits { get; init; }
    public double ReworkUnits { get; init; }
    public double PlannedPerformance { get; init; }
    public HashSet<string> Personnel { get; } = new(StringComparer.CurrentCultureIgnoreCase);
}

public sealed class Mes06OeeAccumulator
{
    public string WorkcenterCode { get; init; } = string.Empty;
    public string ShiftName { get; init; } = string.Empty;
    public DateTime? ShiftStarttime { get; set; }
    public HashSet<string> Personnel { get; } = new(StringComparer.CurrentCultureIgnoreCase);
    public double FailureSeconds { get; set; }
    public double AvailableSeconds { get; set; }
    public double PlannedShutdownSeconds { get; set; }
    public double TotalAmount { get; set; }
    public double GoodUnits { get; set; }
    public double BadUnits { get; set; }
    public double ReworkUnits { get; set; }
    public double PlannedPerformanceWeighted { get; set; }
    public double PlannedPerformanceWeight { get; set; }
}

public sealed class Mes06OeeReportRecord
{
    public string WorkcenterCode { get; init; } = string.Empty;
    public string ShiftName { get; init; } = string.Empty;
    public DateTime? ShiftStarttime { get; init; }
    public string Personnel { get; init; } = string.Empty;
    public string HumanCode { get; init; } = string.Empty;
    public double FailureSeconds { get; init; }
    public double AvailableSeconds { get; init; }
    public double PlannedShutdownSeconds { get; init; }
    public double TotalAmount { get; init; }
    public double GoodUnits { get; init; }
    public double BadUnits { get; init; }
    public double ReworkUnits { get; init; }
    public double ActualPerformance { get; init; }
    public double PlannedPerformance { get; init; }
    public double EquipmentUtilization { get; init; }
    public double AvailabilityOee { get; init; }
    public double PerformanceOee { get; init; }
    public double QualityOee { get; init; }
    public double PerformanceBenefit { get; init; }
    public double Oee { get; init; }
    public double Nee { get; init; }
    public double Teep { get; init; }
    public double AvailabilityLoss { get; init; }
    public double PerformanceLoss { get; init; }
    public double QualityLoss { get; init; }

    public string FailureText => FormatDuration(FailureSeconds);
    public string AvailableText => FormatDuration(AvailableSeconds);
    public string PlannedShutdownText => FormatDuration(PlannedShutdownSeconds);
    public string TotalAmountText => Math.Round(TotalAmount).ToString("0");
    public string GoodUnitsText => Math.Round(GoodUnits).ToString("0");
    public string BadUnitsText => Math.Round(BadUnits).ToString("0");
    public string ReworkUnitsText => Math.Round(ReworkUnits).ToString("0");
    public string ActualPerformanceText => $"{ActualPerformance:0.#}";
    public string PlannedPerformanceText => $"{PlannedPerformance:0.#}";
    public string EquipmentUtilizationText => $"{EquipmentUtilization:0.00}%";
    public string AvailabilityOeeText => $"{AvailabilityOee:0.00}%";
    public string PerformanceOeeText => $"{PerformanceOee:0.00}%";
    public string QualityOeeText => $"{QualityOee:0.00}%";
    public string PerformanceBenefitText => $"{PerformanceBenefit:0.00}%";
    public string OeeText => $"{Oee:0.00}%";
    public string NeeText => $"{Nee:0.00}%";
    public string TeepText => $"{Teep:0.00}%";

    public static string FormatPersonnel(string lastName, string firstName, string humanCode)
    {
        var name = string.Join(", ", new[] { lastName, firstName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(name))
        {
            return !string.IsNullOrWhiteSpace(humanCode) ? $"[{humanCode}]" : "—";
        }

        return !string.IsNullOrWhiteSpace(humanCode) ? $"{name} [{humanCode}]" : name;
    }

    public static Mes06OeeReportRecord FromAccumulator(Mes06OeeAccumulator a)
    {
        var actual = a.AvailableSeconds > 0d
            ? a.TotalAmount * 60d / a.AvailableSeconds
            : 0d;
        var planned = a.PlannedPerformanceWeight > 0d
            ? a.PlannedPerformanceWeighted / a.PlannedPerformanceWeight
            : 0d;
        var availability = a.AvailableSeconds + a.FailureSeconds > 0d
            ? a.AvailableSeconds / (a.AvailableSeconds + a.FailureSeconds)
            : 0d;
        var performance = planned > 0d ? actual / planned : 0d;
        var quality = a.TotalAmount > 0d ? a.GoodUnits / a.TotalAmount : 0d;
        var equipment = a.AvailableSeconds + a.FailureSeconds + a.PlannedShutdownSeconds > 0d
            ? (a.AvailableSeconds + a.FailureSeconds)
              / (a.AvailableSeconds + a.FailureSeconds + a.PlannedShutdownSeconds)
            : 0d;
        var oee = availability * performance * quality;
        var perfBenefit = availability * Math.Max(0d, performance - 1d) * quality;

        var personnel = a.Personnel.Count > 0
            ? string.Join("; ", a.Personnel.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
            : "—";

        return new Mes06OeeReportRecord
        {
            WorkcenterCode = a.WorkcenterCode,
            ShiftName = a.ShiftName,
            ShiftStarttime = a.ShiftStarttime,
            Personnel = personnel,
            FailureSeconds = a.FailureSeconds,
            AvailableSeconds = a.AvailableSeconds,
            PlannedShutdownSeconds = a.PlannedShutdownSeconds,
            TotalAmount = a.TotalAmount,
            GoodUnits = a.GoodUnits,
            BadUnits = a.BadUnits,
            ReworkUnits = a.ReworkUnits,
            ActualPerformance = actual,
            PlannedPerformance = planned,
            EquipmentUtilization = equipment * 100d,
            AvailabilityOee = availability * 100d,
            PerformanceOee = performance * 100d,
            QualityOee = quality * 100d,
            PerformanceBenefit = perfBenefit * 100d,
            Oee = oee * 100d,
            Nee = oee * 100d,
            Teep = oee * equipment * 100d,
            AvailabilityLoss = Math.Max(0d, 1d - availability) * 100d,
            PerformanceLoss = availability * Math.Max(0d, 1d - performance) * 100d,
            QualityLoss = availability * performance * Math.Max(0d, 1d - quality) * 100d
        };
    }

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        var hours = (int)value.TotalHours;
        return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}

public sealed class Mes06ProcessValueRecord
{
    public Guid Id { get; init; }
    public Guid MesId { get; init; }
    public string WorkcenterCode { get; init; } = string.Empty;
    public string Level => "Info";
    public string StateName { get; init; } = string.Empty;
    public DateTime Starttime { get; init; }
    public DateTime Endtime { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ShiftName { get; init; } = string.Empty;
    public string DurationText
    {
        get
        {
            var value = Endtime - Starttime;
            var days = (int)value.TotalDays;
            return days > 0
                ? $"{days}d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}"
                : $"{value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }
    }
}

public sealed class Mes06ProductionGraphRecord
{
    public Guid MesId { get; init; }
    public string WorkcenterCode { get; init; } = string.Empty;
    public DateTime Starttime { get; init; }
    public DateTime Endtime { get; init; }
    public decimal? StateDurationSeconds { get; init; }
    public string StateName { get; init; } = string.Empty;
    public int? Availability { get; init; }
    public decimal? DurationUtilizationSeconds { get; init; }
    public decimal? PerformanceTotal { get; init; }
    public decimal? PerformanceGood { get; init; }
    public decimal? PerformanceBad { get; init; }
    public decimal? PerformanceRework { get; init; }
    public decimal? PlannedPerformance { get; init; }
    public string OrderCode { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;

    // Friendly aliases allow the existing States definition / Excel export
    // to display useful raw rows without introducing a second export model.
    public DateTime From => Starttime;
    public DateTime To => Endtime;
    public string Workcenter => WorkcenterCode;
    public string State => StateName;
    public decimal DurationMinutes =>
        StateDurationSeconds.HasValue
            ? StateDurationSeconds.Value / 60m
            : (decimal)(Endtime - Starttime).TotalMinutes;

    /// <summary>
    /// Physical machine tact: total pieces include good + scrap because both
    /// physically pass through the machine. FASTEC MDA utilization is seconds.
    /// </summary>
    public decimal? MachineRateKsMin =>
        DurationUtilizationSeconds.HasValue
        && DurationUtilizationSeconds.Value > 0m
        && PerformanceTotal.HasValue
            ? PerformanceTotal.Value
              * 60m
              / DurationUtilizationSeconds.Value
            : null;
}

public sealed class MesReportingStateColor
{
    public string WorkcenterCode { get; init; } = string.Empty;
    public string StateName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string StateColor { get; init; } = string.Empty;
    public string CategoryColor { get; init; } = string.Empty;
}

public sealed class MesReportingShiftEvent
{
    public Guid? Id { get; init; }
    public DateTime Starttime { get; init; }
    public DateTime Endtime { get; init; }
    public string Name { get; init; } = string.Empty;
}
