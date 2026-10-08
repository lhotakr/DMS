using DMS.Integration.Mes.Database;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;
using DMS.Core.Sap;

namespace DMS.Integration.Mes.Reporting;

/// <summary>
/// Read-only MES06 source for the production "Plachta" report.
/// One result row represents one workcenter + shift + order + operation + product.
/// </summary>
public sealed class MesPlachtaReportService
{
    public const string ReportCode = "PLACHTA";

    private const double DominantShiftThreshold = 0.80d;

    private readonly MesDatabaseConnectionSettings _settings;
    private readonly IMesSqlConnectionFactory _connectionFactory;
    private readonly IReadOnlyDictionary<string, string> _sapDescriptions;
    private readonly string _schema;

    public MesPlachtaReportService(
        MesDatabaseConnectionSettings settings,
        IMesSqlConnectionFactory? connectionFactory = null,
        string? sapMaterialsFilePath = null)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(nameof(settings));

        _settings.Normalize();

        _connectionFactory =
            connectionFactory
            ?? new MesSqlConnectionFactory();

        _schema =
            MesConnectionHealthService.ValidateIdentifier(
                _settings.ReportingSchema);
        _sapDescriptions =
            string.IsNullOrWhiteSpace(sapMaterialsFilePath)
            || !File.Exists(sapMaterialsFilePath)
                ? new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                : new JsonSapMaterialRepository(
                        sapMaterialsFilePath)
                    .LoadAll()
                    .Where(material =>
                        !string.IsNullOrWhiteSpace(
                            material.MaterialNumber))
                    .GroupBy(
                        material => material.MaterialNumber.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Description
                            ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<MesPlachtaReportRecord>> GetReportAsync(
        DateTime from,
        DateTime to,
        IReadOnlyList<string> workcenterCodes,
        string shiftCode,
        string orderCode,
        string operationCode,
        string productCode,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            to = from.AddDays(1);
        }

        // Never count an open MES interval into the future.
        var effectiveTo =
            to > DateTime.Now
                ? DateTime.Now
                : to;

        if (effectiveTo <= from)
        {
            return Array.Empty<MesPlachtaReportRecord>();
        }

        var selectedWorkcenters =
            (workcenterCodes ?? Array.Empty<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray();

        var filter =
            new PlachtaSqlFilter(
                from,
                effectiveTo,
                selectedWorkcenters,
                shiftCode?.Trim() ?? string.Empty,
                orderCode?.Trim() ?? string.Empty,
                operationCode?.Trim() ?? string.Empty,
                productCode?.Trim() ?? string.Empty);

        var accumulators =
            await LoadBasePeriodsAsync(
                filter,
                cancellationToken);

        if (accumulators.Count == 0)
        {
            return Array.Empty<MesPlachtaReportRecord>();
        }

        await LoadPersonnelAsync(
            filter,
            accumulators,
            cancellationToken);

        var availabilityMapping =
            await LoadStateDurationsAsync(
                filter,
                accumulators,
                cancellationToken);

        await LoadCountersAsync(
            filter,
            accumulators,
            cancellationToken);

        return accumulators.Values
            .Select(accumulator =>
                ToRecord(
                    accumulator,
                    availabilityMapping))
            .OrderBy(row => row.WorkcenterCode, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.ShiftStart)
            .ThenBy(row => row.OrderCode, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.OperationCode, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Clamp(maxRows, 1, 50000))
            .ToList();
    }

    private async Task<Dictionary<PlachtaKey, PlachtaAccumulator>> LoadBasePeriodsAsync(
        PlachtaSqlFilter filter,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _connectionFactory
                .OpenAsync(
                    _settings,
                    cancellationToken)
                .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _settings.CommandTimeoutSeconds;

        var workcenterClause =
            AddWorkcenterParameters(
                command,
                filter.WorkcenterCodes,
                "@plBaseWc");

        command.CommandText =
            $"""
            SELECT
                mes.[ID] AS MesID,
                wc.[Code] AS WorkcenterCode,
                sh.[Name] AS ShiftName,
                sh.[Starttime] AS ShiftStart,
                COALESCE(sh.[Endtime], DATEADD(HOUR, 8, sh.[Starttime])) AS ShiftEnd,
                mes.[Starttime],
                mes.[Endtime],
                mes.[DurationUnoccupied],
                mes.[DurationUtilization],
                mes.[DurationDown],
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                op.[OrderQuantity],
                meta.[SapArticleNumber],
                meta.[RoutingDescription]
            FROM [{_schema}].[FactMdaMes] mes
            LEFT JOIN [{_schema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_schema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_schema}].[DimShiftEvent] sh
                ON sh.[ID] = mes.[ShiftID]
            OUTER APPLY
            (
                SELECT TOP (1)
                    NULLIF(LTRIM(RTRIM(po.[cf_customer])), '') AS SapArticleNumber,
                    NULLIF(LTRIM(RTRIM(r.[description])), '') AS RoutingDescription
                FROM dbo.[d_pda_po] po
                LEFT JOIN dbo.[d_pda_po_routing] r
                    ON r.[production_order_id] = po.[id]
                   AND r.[code] = op.[OperationCode]
                WHERE po.[code] = op.[OrderCode]
                ORDER BY po.[change_id] DESC
            ) meta
            WHERE mes.[Starttime] < @to
              AND COALESCE(mes.[Endtime], @to) > @from
              AND NULLIF(LTRIM(RTRIM(op.[OrderCode])), '') IS NOT NULL
              {workcenterClause}
              AND (@shiftCode = '' OR sh.[Name] = @shiftCode)
              AND (@orderCode = '' OR op.[OrderCode] LIKE '%' + @orderCode + '%')
              AND (@operationCode = '' OR op.[OperationCode] LIKE '%' + @operationCode + '%')
              AND (@productCode = '' OR op.[ProductCode] LIKE '%' + @productCode + '%')
            ORDER BY
                wc.[Code],
                sh.[Starttime],
                op.[OrderCode],
                op.[OperationCode],
                mes.[Starttime];
            """;

        AddCommonParameters(
            command,
            filter);

        var result =
            new Dictionary<PlachtaKey, PlachtaAccumulator>();

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            var start =
                GetDateTime(
                    reader,
                    "Starttime");

            if (!start.HasValue)
            {
                continue;
            }

            var rawEnd =
                GetDateTime(
                    reader,
                    "Endtime")
                ?? filter.To;

            if (rawEnd <= start.Value)
            {
                continue;
            }

            var clippedStart =
                start.Value < filter.From
                    ? filter.From
                    : start.Value;

            var clippedEnd =
                rawEnd > filter.To
                    ? filter.To
                    : rawEnd;

            if (clippedEnd <= clippedStart)
            {
                continue;
            }

            var rawSeconds =
                (rawEnd - start.Value)
                    .TotalSeconds;

            var clippedSeconds =
                (clippedEnd - clippedStart)
                    .TotalSeconds;

            if (rawSeconds <= 0d
                || clippedSeconds <= 0d)
            {
                continue;
            }

            var workcenter =
                GetString(
                    reader,
                    "WorkcenterCode");

            var shiftName =
                GetString(
                    reader,
                    "ShiftName");

            var shiftStart =
                GetDateTime(
                    reader,
                    "ShiftStart")
                ?? clippedStart;

            var shiftEnd =
                GetDateTime(
                    reader,
                    "ShiftEnd")
                ?? shiftStart.AddHours(8);

            var order =
                GetString(
                    reader,
                    "OrderCode");

            var operation =
                GetString(
                    reader,
                    "OperationCode");

            var product =
                GetString(
                    reader,
                    "ProductCode");

            var key =
                BuildKey(
                    workcenter,
                    shiftName,
                    shiftStart,
                    order,
                    operation,
                    product);

            if (!result.TryGetValue(
                    key,
                    out var accumulator))
            {
                accumulator =
                    new PlachtaAccumulator
                    {
                        WorkcenterCode = workcenter,
                        ShiftName = shiftName,
                        ShiftStart = shiftStart,
                        ShiftEnd = shiftEnd,
                        OrderCode = order,
                        OperationCode = operation,
                        ProductCode = product,
                        OrderQuantity =
                            GetDecimal(
                                reader,
                                "OrderQuantity"),
                        SapNumber =
                            GetString(
                                reader,
                                "SapArticleNumber"),
                        PlannedPerformance =
                            GetString(
                                reader,
                                "RoutingDescription")
                    };

                result[key] =
                    accumulator;
            }
            else
            {
                accumulator.OrderQuantity ??=
                    GetDecimal(
                        reader,
                        "OrderQuantity");

                if (string.IsNullOrWhiteSpace(
                        accumulator.SapNumber))
                {
                    accumulator.SapNumber =
                        GetString(
                            reader,
                            "SapArticleNumber");
                }

                if (string.IsNullOrWhiteSpace(
                        accumulator.PlannedPerformance))
                {
                    accumulator.PlannedPerformance =
                        GetString(
                            reader,
                            "RoutingDescription");
                }
            }

            var fraction =
                Math.Clamp(
                    clippedSeconds / rawSeconds,
                    0d,
                    1d);

            // Keep FASTEC's aggregate MDA durations as a compatibility fallback.
            // v6 prefers the per-state database classification from
            // machine_state_vector.availability and only falls back to these
            // values if the mapping cannot be resolved safely for the period.
            accumulator.AggregatedUtilizationSeconds +=
                GetDouble(
                    reader,
                    "DurationUtilization")
                * fraction;

            accumulator.AggregatedPlannedShutdownSeconds +=
                GetDouble(
                    reader,
                    "DurationUnoccupied")
                * fraction;

            accumulator.AggregatedFailureSeconds +=
                GetDouble(
                    reader,
                    "DurationDown")
                * fraction;
        }

        return result;
    }

    private async Task LoadPersonnelAsync(
        PlachtaSqlFilter filter,
        Dictionary<PlachtaKey, PlachtaAccumulator> accumulators,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _connectionFactory
                .OpenAsync(
                    _settings,
                    cancellationToken)
                .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _settings.CommandTimeoutSeconds;

        var workcenterClause =
            AddWorkcenterParameters(
                command,
                filter.WorkcenterCodes,
                "@plHumanWc");

        command.CommandText =
            $"""
            SELECT DISTINCT
                wc.[Code] AS WorkcenterCode,
                sh.[Name] AS ShiftName,
                sh.[Starttime] AS ShiftStart,
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                h.[HumanCode],
                h.[FirstName],
                h.[LastName]
            FROM [{_schema}].[FactHResLinkExt] h
            INNER JOIN [{_schema}].[FactMdaMes] mes
                ON mes.[ID] = h.[MesID]
            LEFT JOIN [{_schema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_schema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_schema}].[DimShiftEvent] sh
                ON sh.[ID] = mes.[ShiftID]
            WHERE mes.[Starttime] < @to
              AND COALESCE(mes.[Endtime], @to) > @from
              AND COALESCE(h.[Starttime], mes.[Starttime]) < @to
              AND COALESCE(h.[Endtime], @to) > @from
              {workcenterClause}
              AND (@shiftCode = '' OR sh.[Name] = @shiftCode)
              AND (@orderCode = '' OR op.[OrderCode] LIKE '%' + @orderCode + '%')
              AND (@operationCode = '' OR op.[OperationCode] LIKE '%' + @operationCode + '%')
              AND (@productCode = '' OR op.[ProductCode] LIKE '%' + @productCode + '%')
              AND (
                    NULLIF(LTRIM(RTRIM(h.[HumanCode])), '') IS NOT NULL
                    OR NULLIF(LTRIM(RTRIM(h.[FirstName])), '') IS NOT NULL
                    OR NULLIF(LTRIM(RTRIM(h.[LastName])), '') IS NOT NULL
                  );
            """;

        AddCommonParameters(
            command,
            filter);

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            var shiftStart =
                GetDateTime(
                    reader,
                    "ShiftStart");

            if (!shiftStart.HasValue)
            {
                continue;
            }

            var key =
                BuildKey(
                    GetString(reader, "WorkcenterCode"),
                    GetString(reader, "ShiftName"),
                    shiftStart.Value,
                    GetString(reader, "OrderCode"),
                    GetString(reader, "OperationCode"),
                    GetString(reader, "ProductCode"));

            if (!accumulators.TryGetValue(
                    key,
                    out var accumulator))
            {
                continue;
            }

            var personKey =
                FirstNonEmpty(
                    GetString(reader, "HumanCode"),
                    string.Join(
                        "|",
                        new[]
                        {
                            GetString(reader, "LastName"),
                            GetString(reader, "FirstName")
                        }
                        .Where(value => !string.IsNullOrWhiteSpace(value))));

            if (!string.IsNullOrWhiteSpace(
                    personKey))
            {
                accumulator.Personnel.Add(
                    personKey);
            }
        }
    }

    private async Task<AvailabilityMapping> LoadStateDurationsAsync(
        PlachtaSqlFilter filter,
        Dictionary<PlachtaKey, PlachtaAccumulator> accumulators,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _connectionFactory
                .OpenAsync(
                    _settings,
                    cancellationToken)
                .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _settings.CommandTimeoutSeconds;

        var workcenterClause =
            AddWorkcenterParameters(
                command,
                filter.WorkcenterCodes,
                "@plStateWc");

        // IMPORTANT: availability is the FASTEC state classification configured
        // directly on the state definition (Utilization / Failure / Planned shutdown).
        // We read the raw machine_state_vector because this is the same source already
        // used by the MES production graph and it preserves that classification.
        command.CommandText =
            $"""
            SELECT
                wc.[Code] AS WorkcenterCode,
                sh.[Name] AS ShiftName,
                sh.[Starttime] AS ShiftStart,
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                v.[starttime] AS Starttime,
                v.[endtime] AS Endtime,
                v.[state_name] AS StateName,
                v.[availability] AS Availability,
                stateDef.[StateDescription],
                stateDef.[CategoryName],
                stateDef.[IsCauselessFailure]
            FROM dbo.[machine_state_vector] v
            INNER JOIN [{_schema}].[FactMdaMes] mes
                ON mes.[ID] = v.[mes_id]
            LEFT JOIN [{_schema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_schema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_schema}].[DimShiftEvent] sh
                ON sh.[ID] = mes.[ShiftID]
            OUTER APPLY
            (
                SELECT TOP (1)
                    sd.[Description] AS StateDescription,
                    sd.[CategoryName],
                    sd.[IsCauselessFailure]
                FROM [{_schema}].[DimMdaState] sd
                WHERE sd.[WorkcenterID] = mes.[WorkcenterID]
                  AND sd.[Name] = v.[state_name]
                ORDER BY
                    CASE WHEN sd.[IsActive] = 1 THEN 0 ELSE 1 END,
                    sd.[ID] DESC
            ) stateDef
            WHERE v.[starttime] < @to
              AND COALESCE(v.[endtime], @to) > @from
              {workcenterClause}
              AND (@shiftCode = '' OR sh.[Name] = @shiftCode)
              AND (@orderCode = '' OR op.[OrderCode] LIKE '%' + @orderCode + '%')
              AND (@operationCode = '' OR op.[OperationCode] LIKE '%' + @operationCode + '%')
              AND (@productCode = '' OR op.[ProductCode] LIKE '%' + @productCode + '%');
            """;

        AddCommonParameters(
            command,
            filter);

        var intervals =
            new List<PlachtaStateInterval>();

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            var shiftStart =
                GetDateTime(
                    reader,
                    "ShiftStart");

            var stateStart =
                GetDateTime(
                    reader,
                    "Starttime");

            if (!shiftStart.HasValue
                || !stateStart.HasValue)
            {
                continue;
            }

            var stateEnd =
                GetDateTime(
                    reader,
                    "Endtime")
                ?? filter.To;

            var clippedStart =
                stateStart.Value < filter.From
                    ? filter.From
                    : stateStart.Value;

            var clippedEnd =
                stateEnd > filter.To
                    ? filter.To
                    : stateEnd;

            if (clippedEnd <= clippedStart)
            {
                continue;
            }

            var key =
                BuildKey(
                    GetString(reader, "WorkcenterCode"),
                    GetString(reader, "ShiftName"),
                    shiftStart.Value,
                    GetString(reader, "OrderCode"),
                    GetString(reader, "OperationCode"),
                    GetString(reader, "ProductCode"));

            if (!accumulators.TryGetValue(
                    key,
                    out var accumulator))
            {
                continue;
            }

            var durationSeconds =
                (clippedEnd - clippedStart)
                    .TotalSeconds;

            if (durationSeconds <= 0d)
            {
                continue;
            }

            var stateName =
                GetString(
                    reader,
                    "StateName");

            var isCauselessFailure =
                GetBool(
                    reader,
                    "IsCauselessFailure");

            // Live FASTEC master-data check (QueryResult, 2026-09-30) shows that
            // "Přestavba na novou zakázku" is a Planned Stop but IsSetup=0.
            // For the Plachta column we therefore follow the configured state name
            // instead of the generic IsSetup flag. CategoryName is not stable either
            // (it varies between "Přestavba" and "Technický prostoj").
            if (IsPlachtaSetupState(stateName))
            {
                accumulator.SetupSeconds +=
                    durationSeconds;
            }

            if (ContainsReplacementMachineMarker(
                    stateName,
                    GetString(reader, "StateDescription"),
                    GetString(reader, "CategoryName")))
            {
                accumulator.ReplacementMachineSeconds +=
                    durationSeconds;
            }

            intervals.Add(
                new PlachtaStateInterval(
                    key,
                    GetNullableInt(
                        reader,
                        "Availability"),
                    durationSeconds,
                    stateName,
                    isCauselessFailure));
        }

        // Verified directly against FASTEC machine_state_vector on 2026-09-30:
        // 1 = Utilization time, 2 = Failure time, 3 = Planned shutdown.
        // Keep aggregate MDA values only as an independent source for diagnostics;
        // the Plachta classification itself follows the configured FASTEC state flag.
        var mapping =
            new AvailabilityMapping(
                UtilizationCode: 1,
                FailureCode: 2,
                PlannedShutdownCode: 3,
                HasUtilizationTarget: true,
                HasFailureTarget: true,
                HasPlannedShutdownTarget: true);

        foreach (var interval in intervals)
        {
            if (!accumulators.TryGetValue(
                    interval.Key,
                    out var accumulator))
            {
                continue;
            }

            if (mapping.UtilizationCode.HasValue
                && interval.Availability == mapping.UtilizationCode)
            {
                accumulator.ClassifiedUtilizationSeconds +=
                    interval.DurationSeconds;
            }
            else if (mapping.FailureCode.HasValue
                     && interval.Availability == mapping.FailureCode)
            {
                accumulator.ClassifiedFailureSeconds +=
                    interval.DurationSeconds;
            }
            else if (mapping.PlannedShutdownCode.HasValue
                     && interval.Availability == mapping.PlannedShutdownCode)
            {
                accumulator.ClassifiedPlannedShutdownSeconds +=
                    interval.DurationSeconds;
            }

            // FASTEC currently classifies "Neodůvodněný prostoj" as Availability=2
            // (Failure time). Do not count it twice. This extra branch remains only
            // as a defensive fallback for a future misconfigured state definition.
            if (interval.IsCauselessFailure
                && (!mapping.FailureCode.HasValue
                    || interval.Availability != mapping.FailureCode))
            {
                accumulator.CauselessFailureExtraSeconds +=
                    interval.DurationSeconds;
            }
        }

        return mapping;
    }

    private async Task LoadCountersAsync(
        PlachtaSqlFilter filter,
        Dictionary<PlachtaKey, PlachtaAccumulator> accumulators,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _connectionFactory
                .OpenAsync(
                    _settings,
                    cancellationToken)
                .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _settings.CommandTimeoutSeconds;

        var workcenterClause =
            AddWorkcenterParameters(
                command,
                filter.WorkcenterCodes,
                "@plCounterWc");

        command.CommandText =
            $"""
            SELECT
                wc.[Code] AS WorkcenterCode,
                sh.[Name] AS ShiftName,
                sh.[Starttime] AS ShiftStart,
                op.[OrderCode],
                op.[OperationCode],
                op.[ProductCode],
                dc.[Name] AS CounterName,
                c.[Value]
            FROM [{_schema}].[FactMdaCounter] c
            INNER JOIN [{_schema}].[DimMdaCounter] dc
                ON dc.[ID] = c.[CounterID]
            INNER JOIN [{_schema}].[FactMdaMes] mes
                ON mes.[ID] = c.[MesID]
            LEFT JOIN [{_schema}].[DimWorkcenter] wc
                ON wc.[ID] = mes.[WorkcenterID]
            LEFT JOIN [{_schema}].[DimMdaOperation] op
                ON op.[ID] = mes.[OperationID]
            LEFT JOIN [{_schema}].[DimShiftEvent] sh
                ON sh.[ID] = mes.[ShiftID]
            WHERE c.[Timestamp] >= @from
              AND c.[Timestamp] < @to
              {workcenterClause}
              AND (@shiftCode = '' OR sh.[Name] = @shiftCode)
              AND (@orderCode = '' OR op.[OrderCode] LIKE '%' + @orderCode + '%')
              AND (@operationCode = '' OR op.[OperationCode] LIKE '%' + @operationCode + '%')
              AND (@productCode = '' OR op.[ProductCode] LIKE '%' + @productCode + '%');
            """;

        AddCommonParameters(
            command,
            filter);

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            var shiftStart =
                GetDateTime(
                    reader,
                    "ShiftStart");

            if (!shiftStart.HasValue)
            {
                continue;
            }

            var key =
                BuildKey(
                    GetString(reader, "WorkcenterCode"),
                    GetString(reader, "ShiftName"),
                    shiftStart.Value,
                    GetString(reader, "OrderCode"),
                    GetString(reader, "OperationCode"),
                    GetString(reader, "ProductCode"));

            if (!accumulators.TryGetValue(
                    key,
                    out var accumulator))
            {
                continue;
            }

            var counterName =
                GetString(
                    reader,
                    "CounterName");

            var value =
                GetDecimal(
                    reader,
                    "Value")
                ?? 0m;

            if (string.Equals(
                    counterName,
                    "Vyrobeno - stroj",
                    StringComparison.OrdinalIgnoreCase))
            {
                accumulator.PrintedGross +=
                    value;
            }
            else if (string.Equals(
                         counterName,
                         "Odpad - produkce",
                         StringComparison.OrdinalIgnoreCase))
            {
                accumulator.ScrapProduction +=
                    value;
            }
            else if (string.Equals(
                         counterName,
                         "Odpad - sklo",
                         StringComparison.OrdinalIgnoreCase))
            {
                accumulator.ScrapGlass +=
                    value;
            }
            else if (string.Equals(
                         counterName,
                         "Myté flakony",
                         StringComparison.OrdinalIgnoreCase))
            {
                accumulator.WashedBottles +=
                    value;
            }
        }
    }

    private MesPlachtaReportRecord ToRecord(
        PlachtaAccumulator accumulator,
        AvailabilityMapping availabilityMapping)
    {
        var shiftSeconds =
            accumulator.ShiftEnd > accumulator.ShiftStart
                ? (accumulator.ShiftEnd - accumulator.ShiftStart).TotalSeconds
                : 8d * 60d * 60d;

        var totalScrap =
            accumulator.ScrapProduction
            + accumulator.ScrapGlass;

        var printedNet =
            accumulator.PrintedGross
            - totalScrap
            - accumulator.WashedBottles;

        var scrapPercent =
            accumulator.PrintedGross != 0m
                ? totalScrap / accumulator.PrintedGross * 100m
                : 0m;

        var productionSeconds =
            availabilityMapping.CanUseProductionClassification
                ? accumulator.ClassifiedUtilizationSeconds
                  + accumulator.ClassifiedPlannedShutdownSeconds
                : accumulator.AggregatedUtilizationSeconds
                  + accumulator.AggregatedPlannedShutdownSeconds;

        var downtimeSeconds =
            availabilityMapping.CanUseFailureClassification
                ? accumulator.ClassifiedFailureSeconds
                  + accumulator.CauselessFailureExtraSeconds
                : accumulator.AggregatedFailureSeconds;

        var productDescription =
            string.Empty;

        if (!string.IsNullOrWhiteSpace(
                accumulator.SapNumber)
            && _sapDescriptions.TryGetValue(
                accumulator.SapNumber.Trim(),
                out var sapDescription))
        {
            productDescription =
                sapDescription;
        }

        return new MesPlachtaReportRecord
        {
            ShiftStart =
                accumulator.ShiftStart,
            Starttime =
                accumulator.ShiftStart,
            WorkcenterCode =
                accumulator.WorkcenterCode,
            ShiftName =
                accumulator.ShiftName,
            ShiftCode =
                ToRomanShift(
                    accumulator.ShiftName),
            BaanNumber =
                accumulator.ProductCode,
            SapNumber =
                accumulator.SapNumber,
            ProductDescription =
                productDescription,
            OrderCode =
                accumulator.OrderCode,
            OrderQuantity =
                accumulator.OrderQuantity,
            OperationCode =
                accumulator.OperationCode,
            PlannedPerformance =
                accumulator.PlannedPerformance,
            SetupMark =
                accumulator.SetupSeconds >=
                shiftSeconds * DominantShiftThreshold
                    ? "×"
                    : string.Empty,
            ReplacementMachineMark =
                accumulator.ReplacementMachineSeconds >=
                shiftSeconds * DominantShiftThreshold
                    ? "×"
                    : string.Empty,
            PersonnelCount =
                accumulator.Personnel.Count,
            ProductionTimeHours =
                productionSeconds / 3600d,
            DowntimeHours =
                downtimeSeconds / 3600d,
            PrintedGross =
                accumulator.PrintedGross,
            PrintedNet =
                printedNet,
            TotalScrap =
                totalScrap,
            ScrapPercent =
                scrapPercent,
            Notes =
                string.Empty
        };
    }

    private static string ToRomanShift(
        string shiftName)
    {
        var normalized =
            RemoveDiacritics(
                    shiftName)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Contains("ranni", StringComparison.Ordinal)
            || normalized.Contains("morning", StringComparison.Ordinal)
            || normalized.Contains("fruh", StringComparison.Ordinal))
        {
            return "I.";
        }

        if (normalized.Contains("odpoled", StringComparison.Ordinal)
            || normalized.Contains("afternoon", StringComparison.Ordinal)
            || normalized.Contains("spat", StringComparison.Ordinal))
        {
            return "II.";
        }

        if (normalized.Contains("nocni", StringComparison.Ordinal)
            || normalized.Contains("night", StringComparison.Ordinal)
            || normalized.Contains("nacht", StringComparison.Ordinal))
        {
            return "III.";
        }

        return string.IsNullOrWhiteSpace(shiftName)
            ? string.Empty
            : shiftName.Trim();
    }

    private static bool IsPlachtaSetupState(
        string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        var normalized =
            RemoveDiacritics(stateName)
                .Trim()
                .ToLowerInvariant();

        // Verified in ana.DimMdaState on 2026-09-30.
        return string.Equals(
            normalized,
            "prestavba na novou zakazku",
            StringComparison.Ordinal);
    }

    private static bool ContainsReplacementMachineMarker(
        params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized =
                RemoveDiacritics(value)
                    .ToLowerInvariant();

            if (normalized.Contains("nahradni stroj", StringComparison.Ordinal)
                || normalized.Contains("replacement machine", StringComparison.Ordinal)
                || normalized.Contains("ersatzmaschine", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveDiacritics(
        string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized =
            value.Normalize(
                NormalizationForm.FormD);

        var chars =
            normalized
                .Where(ch =>
                    CharUnicodeInfo.GetUnicodeCategory(ch)
                    != UnicodeCategory.NonSpacingMark)
                .ToArray();

        return new string(chars)
            .Normalize(
                NormalizationForm.FormC);
    }

    private static PlachtaKey BuildKey(
        string workcenterCode,
        string shiftName,
        DateTime shiftStart,
        string orderCode,
        string operationCode,
        string productCode) =>
        new(
            NormalizeKeyPart(workcenterCode),
            NormalizeKeyPart(shiftName),
            shiftStart,
            NormalizeKeyPart(orderCode),
            NormalizeKeyPart(operationCode),
            NormalizeKeyPart(productCode));

    private static string NormalizeKeyPart(
        string value) =>
        value?.Trim().ToUpperInvariant()
        ?? string.Empty;

    private static AvailabilityMapping ResolveAvailabilityMapping(
        IReadOnlyList<PlachtaStateInterval> intervals,
        IReadOnlyDictionary<PlachtaKey, PlachtaAccumulator> accumulators)
    {
        var candidates =
            intervals
                .Where(interval => interval.Availability.HasValue)
                .Select(interval => interval.Availability!.Value)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

        if (candidates.Length == 0)
        {
            return AvailabilityMapping.Empty;
        }

        var targetTotals =
            new[]
            {
                accumulators.Values.Sum(x => x.AggregatedUtilizationSeconds),
                accumulators.Values.Sum(x => x.AggregatedFailureSeconds),
                accumulators.Values.Sum(x => x.AggregatedPlannedShutdownSeconds)
            };

        var requiredTargets =
            Enumerable.Range(0, 3)
                .Where(index => targetTotals[index] > 1d)
                .ToArray();

        if (requiredTargets.Length == 0)
        {
            return AvailabilityMapping.Empty;
        }

        var durations =
            intervals
                .Where(interval => interval.Availability.HasValue)
                .GroupBy(interval =>
                    (interval.Key, Code: interval.Availability!.Value))
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(interval => interval.DurationSeconds));

        double Score(
            int targetIndex,
            int candidate)
        {
            double absoluteError = 0d;
            double targetTotal = 0d;

            foreach (var pair in accumulators)
            {
                var target =
                    targetIndex switch
                    {
                        0 => pair.Value.AggregatedUtilizationSeconds,
                        1 => pair.Value.AggregatedFailureSeconds,
                        2 => pair.Value.AggregatedPlannedShutdownSeconds,
                        _ => 0d
                    };

                if (target <= 1d)
                {
                    continue;
                }

                durations.TryGetValue(
                    (pair.Key, candidate),
                    out var actual);

                absoluteError +=
                    Math.Abs(actual - target);

                targetTotal +=
                    target;
            }

            return targetTotal > 0d
                ? absoluteError / targetTotal
                : double.MaxValue;
        }

        var bestCodes =
            new int?[3];

        var bestScores =
            new[]
            {
                double.MaxValue,
                double.MaxValue,
                double.MaxValue
            };

        var bestTotalScore =
            double.MaxValue;

        void Search(
            int depth,
            HashSet<int> used,
            int?[] selected,
            double[] selectedScores)
        {
            if (depth >= requiredTargets.Length)
            {
                var totalScore =
                    requiredTargets.Sum(index => selectedScores[index]);

                if (totalScore < bestTotalScore)
                {
                    bestTotalScore = totalScore;
                    Array.Copy(selected, bestCodes, 3);
                    Array.Copy(selectedScores, bestScores, 3);
                }

                return;
            }

            var targetIndex =
                requiredTargets[depth];

            foreach (var candidate in candidates)
            {
                if (!used.Add(candidate))
                {
                    continue;
                }

                selected[targetIndex] =
                    candidate;

                selectedScores[targetIndex] =
                    Score(
                        targetIndex,
                        candidate);

                Search(
                    depth + 1,
                    used,
                    selected,
                    selectedScores);

                selected[targetIndex] = null;
                selectedScores[targetIndex] = double.MaxValue;
                used.Remove(candidate);
            }
        }

        if (candidates.Length >= requiredTargets.Length)
        {
            Search(
                0,
                new HashSet<int>(),
                new int?[3],
                new[]
                {
                    double.MaxValue,
                    double.MaxValue,
                    double.MaxValue
                });
        }

        // The state vector and the aggregate MDA values should describe the same
        // physical time. If the relative error is too large, do not guess.
        const double maximumAcceptedRelativeError = 0.35d;

        int? Accept(
            int index) =>
            bestCodes[index].HasValue
            && bestScores[index] <= maximumAcceptedRelativeError
                ? bestCodes[index]
                : null;

        var utilizationCode =
            targetTotals[0] <= 1d
                ? null
                : Accept(0);

        var failureCode =
            targetTotals[1] <= 1d
                ? null
                : Accept(1);

        var plannedShutdownCode =
            targetTotals[2] <= 1d
                ? null
                : Accept(2);

        return new AvailabilityMapping(
            utilizationCode,
            failureCode,
            plannedShutdownCode,
            targetTotals[0] <= 1d || utilizationCode.HasValue,
            targetTotals[1] <= 1d || failureCode.HasValue,
            targetTotals[2] <= 1d || plannedShutdownCode.HasValue);
    }

    private static string AddWorkcenterParameters(
        SqlCommand command,
        IReadOnlyList<string> workcenterCodes,
        string prefix)
    {
        if (workcenterCodes.Count == 0)
        {
            return string.Empty;
        }

        var parameterNames =
            new List<string>(
                workcenterCodes.Count);

        for (var index = 0;
             index < workcenterCodes.Count;
             index++)
        {
            var parameterName =
                $"{prefix}{index}";

            parameterNames.Add(
                parameterName);

            command.Parameters.Add(
                new SqlParameter(
                    parameterName,
                    SqlDbType.NVarChar,
                    255)
                {
                    Value = workcenterCodes[index]
                });
        }

        return
            $"AND wc.[Code] IN ({string.Join(", ", parameterNames)})";
    }

    private static void AddCommonParameters(
        SqlCommand command,
        PlachtaSqlFilter filter)
    {
        command.Parameters.Add(
            new SqlParameter(
                "@from",
                SqlDbType.DateTime2)
            {
                Value = filter.From
            });

        command.Parameters.Add(
            new SqlParameter(
                "@to",
                SqlDbType.DateTime2)
            {
                Value = filter.To
            });

        AddTextParameter(
            command,
            "@shiftCode",
            filter.ShiftCode);

        AddTextParameter(
            command,
            "@orderCode",
            filter.OrderCode);

        AddTextParameter(
            command,
            "@operationCode",
            filter.OperationCode);

        AddTextParameter(
            command,
            "@productCode",
            filter.ProductCode);
    }

    private static void AddTextParameter(
        SqlCommand command,
        string name,
        string value)
    {
        command.Parameters.Add(
            new SqlParameter(
                name,
                SqlDbType.NVarChar,
                255)
            {
                Value = value ?? string.Empty
            });
    }

    private static string GetString(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(ordinal),
                  CultureInfo.InvariantCulture)
              ?.Trim()
              ?? string.Empty;
    }

    private static DateTime? GetDateTime(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value =
            reader.GetValue(
                ordinal);

        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset offset => offset.DateTime,
            _ => DateTime.TryParse(
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
                ? parsed
                : null
        };
    }

    private static decimal? GetDecimal(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToDecimal(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static int? GetNullableInt(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static double GetDouble(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return 0d;
        }

        return Convert.ToDouble(
            reader.GetValue(ordinal),
            CultureInfo.InvariantCulture);
    }

    private static bool GetBool(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal =
            reader.GetOrdinal(
                columnName);

        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        var value =
            reader.GetValue(
                ordinal);

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return Convert.ToInt32(
            value,
            CultureInfo.InvariantCulture) != 0;
    }

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private readonly record struct PlachtaSqlFilter(
        DateTime From,
        DateTime To,
        IReadOnlyList<string> WorkcenterCodes,
        string ShiftCode,
        string OrderCode,
        string OperationCode,
        string ProductCode);

    private readonly record struct PlachtaKey(
        string WorkcenterCode,
        string ShiftName,
        DateTime ShiftStart,
        string OrderCode,
        string OperationCode,
        string ProductCode);

    private readonly record struct PlachtaStateInterval(
        PlachtaKey Key,
        int? Availability,
        double DurationSeconds,
        string StateName,
        bool IsCauselessFailure);

    private readonly record struct AvailabilityMapping(
        int? UtilizationCode,
        int? FailureCode,
        int? PlannedShutdownCode,
        bool HasUtilizationTarget,
        bool HasFailureTarget,
        bool HasPlannedShutdownTarget)
    {
        public static AvailabilityMapping Empty =>
            new(
                null,
                null,
                null,
                false,
                false,
                false);

        public bool CanUseProductionClassification =>
            HasUtilizationTarget
            && HasPlannedShutdownTarget;

        public bool CanUseFailureClassification =>
            HasFailureTarget;
    }

    private sealed class PlachtaAccumulator
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string ShiftName { get; init; } = string.Empty;
        public DateTime ShiftStart { get; init; }
        public DateTime ShiftEnd { get; init; }
        public string OrderCode { get; init; } = string.Empty;
        public string OperationCode { get; init; } = string.Empty;
        public string ProductCode { get; init; } = string.Empty;
        public decimal? OrderQuantity { get; set; }
        public string SapNumber { get; set; } = string.Empty;
        public string PlannedPerformance { get; set; } = string.Empty;
        public HashSet<string> Personnel { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public double AggregatedUtilizationSeconds { get; set; }
        public double AggregatedPlannedShutdownSeconds { get; set; }
        public double AggregatedFailureSeconds { get; set; }
        public double ClassifiedUtilizationSeconds { get; set; }
        public double ClassifiedPlannedShutdownSeconds { get; set; }
        public double ClassifiedFailureSeconds { get; set; }
        public double CauselessFailureExtraSeconds { get; set; }
        public double SetupSeconds { get; set; }
        public double ReplacementMachineSeconds { get; set; }
        public decimal PrintedGross { get; set; }
        public decimal ScrapProduction { get; set; }
        public decimal ScrapGlass { get; set; }
        public decimal WashedBottles { get; set; }
    }
}

public sealed class MesPlachtaReportRecord
{
    // Internal/report-context values. They intentionally stay out of the
    // visible Plachta column definition and scheduler projection.
    public DateTime ShiftStart { get; init; }
    public DateTime Starttime { get; init; }
    public string ShiftName { get; init; } = string.Empty;

    public string WorkcenterCode { get; init; } = string.Empty;
    public string ShiftCode { get; init; } = string.Empty;
    public string BaanNumber { get; init; } = string.Empty;
    public string SapNumber { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;
    public string OrderCode { get; init; } = string.Empty;
    public decimal? OrderQuantity { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public string PlannedPerformance { get; init; } = string.Empty;
    public string SetupMark { get; init; } = string.Empty;
    public string ReplacementMachineMark { get; init; } = string.Empty;
    public int PersonnelCount { get; init; }
    public double ProductionTimeHours { get; init; }
    public double DowntimeHours { get; init; }
    public decimal PrintedGross { get; init; }
    public decimal PrintedNet { get; init; }
    public decimal TotalScrap { get; init; }
    public decimal ScrapPercent { get; init; }
    public string Notes { get; init; } = string.Empty;
}
