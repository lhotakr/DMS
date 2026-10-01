using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Screens
{
    public sealed class Scr10QueueDataService
    {
        private readonly MesDatabaseConnectionSettings _settings;
        private readonly MesSqlConnectionFactory _connectionFactory = new();
        private readonly MesReportingEnrichmentService _reportingEnrichment;
        private readonly string _analyticsSchema;

        public Scr10QueueDataService(MesDatabaseConnectionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Normalize();
            _reportingEnrichment = new MesReportingEnrichmentService(_settings);
            _analyticsSchema = EscapeIdentifier(
                string.IsNullOrWhiteSpace(_settings.ReportingSchema) ? "ana" : _settings.ReportingSchema);
        }

        public async Task<IReadOnlyList<Scr10WorkcenterGroup>> GetWorkcenterGroupsAsync(
            CancellationToken cancellationToken = default)
        {
            var groupsByWorkcenter =
                await _reportingEnrichment.GetWorkcenterGroupsAsync(cancellationToken);

            var byGroup = new Dictionary<string, HashSet<string>>(
                StringComparer.CurrentCultureIgnoreCase);

            foreach (var pair in groupsByWorkcenter)
            {
                var wc = pair.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(wc))
                    continue;

                foreach (var group in pair.Value
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Select(x => x.Trim()))
                {
                    if (!byGroup.TryGetValue(group, out var members))
                    {
                        members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        byGroup[group] = members;
                    }
                    members.Add(wc);
                }
            }

            var result = new List<Scr10WorkcenterGroup>
            {
                new() { Code = string.Empty, WorkcenterCodes = Array.Empty<string>() }
            };

            result.AddRange(byGroup
                .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new Scr10WorkcenterGroup
                {
                    Code = x.Key,
                    WorkcenterCodes = x.Value
                        .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
                        .ToArray()
                }));

            return result;
        }

        public async Task<IReadOnlyList<Scr10MesOrderRow>> GetOrdersAsync(
            Scr10QueueMode mode,
            CancellationToken cancellationToken = default)
        {
            await using var connection =
                await _connectionFactory.OpenAsync(_settings, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandTimeout = _settings.CommandTimeoutSeconds;
            command.CommandText = mode == Scr10QueueMode.Current
                ? BuildCurrentOrdersSql()
                : BuildPlannedOrdersSql();

            var result = new List<Scr10MesOrderRow>();

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new Scr10MesOrderRow
                {
                    WorkcenterCode = ReadString(reader, "WorkcenterCode"),
                    OrderCode = ReadString(reader, "OrderCode"),
                    ProductCode = ReadString(reader, "ProductCode"),
                    SapNumber = ReadString(reader, "SapNumber"),
                    OrderQuantity = ReadDecimal(reader, "OrderQuantity"),
                    ProducedQuantity = ReadDecimal(reader, "ProducedQuantity"),
                    OperationCode = ReadString(reader, "OperationCode"),
                    StatusCode = ReadString(reader, "StatusCode"),
                    QueuePosition = ReadInt32(reader, "QueuePosition")
                });
            }

            return result;
        }

        public async Task<IReadOnlyDictionary<string, Scr10MachineState>> GetCurrentStatesAsync(
            IReadOnlyCollection<string> workcenterCodes,
            CancellationToken cancellationToken = default)
        {
            if (workcenterCodes == null || workcenterCodes.Count == 0)
                return new Dictionary<string, Scr10MachineState>(StringComparer.OrdinalIgnoreCase);

            await using var connection =
                await _connectionFactory.OpenAsync(_settings, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandTimeout = _settings.CommandTimeoutSeconds;

            var names = new List<string>();
            var i = 0;
            foreach (var code in workcenterCodes
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var name = $"@wc{i++}";
                names.Add(name);
                var p = command.CreateParameter();
                p.ParameterName = name;
                p.Value = code.Trim();
                command.Parameters.Add(p);
            }

            if (names.Count == 0)
                return new Dictionary<string, Scr10MachineState>(StringComparer.OrdinalIgnoreCase);

            command.CommandText = BuildCurrentStatesSql(names);

            var result = new Dictionary<string, Scr10MachineState>(
                StringComparer.OrdinalIgnoreCase);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var state = new Scr10MachineState
                {
                    WorkcenterCode = ReadString(reader, "WorkcenterCode"),
                    StateText = ReadString(reader, "StateText", "Neznámý"),
                    StateCategory = ReadString(reader, "StateCategory", "Unknown"),
                    StateColor = ReadString(reader, "StateColor", "#9E9E9E"),
                    StateSince = ReadNullableDateTime(reader, "StateSince")
                };

                if (!string.IsNullOrWhiteSpace(state.WorkcenterCode))
                    result[state.WorkcenterCode] = state;
            }

            return result;
        }

        private string BuildCurrentOrdersSql() => $@"
;WITH CurrentMes AS
(
    SELECT
        mes.[ID] AS MesId,
        wc.[Code] AS WorkcenterCode,
        op.[OrderCode],
        op.[ProductCode],
        op.[OperationCode],
        COALESCE(mes.[PerformanceGood], 0) AS PerformanceGood,
        mes.[Starttime],
        ROW_NUMBER() OVER
        (
            PARTITION BY wc.[Code]
            ORDER BY mes.[Starttime] DESC, mes.[ID] DESC
        ) AS rn
    FROM [{_analyticsSchema}].[FactMdaMes] mes
    INNER JOIN [{_analyticsSchema}].[DimWorkcenter] wc
        ON wc.[ID] = mes.[WorkcenterID]
    LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
        ON op.[ID] = mes.[OperationID]
    WHERE mes.[Endtime] IS NULL
      AND NULLIF(LTRIM(RTRIM(op.[OrderCode])), '') IS NOT NULL
)
SELECT
    cm.[WorkcenterCode] AS WorkcenterCode,
    cm.[OrderCode] AS OrderCode,
    cm.[ProductCode] AS ProductCode,
    COALESCE(NULLIF(LTRIM(RTRIM(po.[cf_customer])), ''), '') AS SapNumber,
    COALESCE(routing.[quantity], 0) AS OrderQuantity,
    COALESCE(routing.[finished_quantity], cm.[PerformanceGood], 0) AS ProducedQuantity,
    cm.[OperationCode] AS OperationCode,
    'REL PROD' AS StatusCode,
    0 AS QueuePosition
FROM CurrentMes cm
LEFT JOIN dbo.[d_pda_po] po
    ON po.[code] = cm.[OrderCode]
OUTER APPLY
(
    SELECT TOP (1)
        r.[quantity],
        r.[finished_quantity]
    FROM dbo.[d_pda_po_routing] r
    WHERE r.[production_order_id] = po.[id]
      AND
      (
          NULLIF(LTRIM(RTRIM(cm.[OperationCode])), '') IS NULL
          OR r.[code] = cm.[OperationCode]
      )
    ORDER BY
        CASE WHEN r.[code] = cm.[OperationCode] THEN 0 ELSE 1 END,
        TRY_CONVERT(int, r.[code]),
        r.[code],
        r.[id]
) routing
WHERE cm.[rn] = 1
ORDER BY cm.[WorkcenterCode], cm.[OperationCode];";

        private string BuildPlannedOrdersSql() => $@"
;WITH RunningOperations AS
(
    SELECT DISTINCT
        op.[OrderCode],
        op.[OperationCode],
        wc.[Code] AS WorkcenterCode
    FROM [{_analyticsSchema}].[FactMdaMes] mes
    INNER JOIN [{_analyticsSchema}].[DimWorkcenter] wc
        ON wc.[ID] = mes.[WorkcenterID]
    LEFT JOIN [{_analyticsSchema}].[DimMdaOperation] op
        ON op.[ID] = mes.[OperationID]
    WHERE mes.[Endtime] IS NULL
),
ForecastBase AS
(
    SELECT
        LTRIM(RTRIM(r.[cost_center])) AS WorkcenterCode,
        po.[code] AS OrderCode,
        po.[product_code] AS ProductCode,
        COALESCE(NULLIF(LTRIM(RTRIM(po.[cf_customer])), ''), '') AS SapNumber,
        COALESCE(r.[quantity], 0) AS OrderQuantity,
        COALESCE(r.[finished_quantity], 0) AS ProducedQuantity,
        r.[code] AS OperationCode,
        r.[planned_start_date] AS PlannedStart,
        r.[id] AS OperationId,
        CASE WHEN running.[OrderCode] IS NOT NULL THEN 'REL PROD' ELSE 'REL' END AS StatusCode
    FROM dbo.[d_pda_po] po
    INNER JOIN dbo.[d_pda_po_routing] r
        ON r.[production_order_id] = po.[id]
    LEFT JOIN RunningOperations running
        ON running.[OrderCode] = po.[code]
       AND running.[OperationCode] = r.[code]
       AND running.[WorkcenterCode] = LTRIM(RTRIM(r.[cost_center]))
    WHERE COALESCE(po.[archive_status], 0) = 0
      AND NULLIF(LTRIM(RTRIM(po.[code])), '') IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(r.[cost_center])), '') IS NOT NULL
      AND r.[actual_end_date] IS NULL
      AND COALESCE(r.[finished_quantity], 0) < COALESCE(r.[quantity], 0)
),
ForecastQueue AS
(
    SELECT
        WorkcenterCode,
        OrderCode,
        ProductCode,
        SapNumber,
        OrderQuantity,
        ProducedQuantity,
        OperationCode,
        StatusCode,
        ROW_NUMBER() OVER
        (
            PARTITION BY WorkcenterCode
            ORDER BY
                CASE WHEN StatusCode = 'REL PROD' THEN 0 ELSE 1 END,
                CASE WHEN PlannedStart IS NULL THEN 1 ELSE 0 END,
                PlannedStart,
                OrderCode,
                TRY_CONVERT(int, OperationCode),
                OperationCode,
                OperationId
        ) AS QueuePosition
    FROM ForecastBase
)
SELECT
    WorkcenterCode,
    OrderCode,
    ProductCode,
    SapNumber,
    OrderQuantity,
    ProducedQuantity,
    OperationCode,
    StatusCode,
    CONVERT(int, QueuePosition) AS QueuePosition
FROM ForecastQueue
ORDER BY WorkcenterCode, QueuePosition, OperationCode;";

        private string BuildCurrentStatesSql(
            IReadOnlyCollection<string> parameterNames) => $@"
;WITH LatestState AS
(
    SELECT
        wc.[Code] AS WorkcenterCode,
        ms.[availability],
        ms.[starttime] AS StateSince,
        ROW_NUMBER() OVER
        (
            PARTITION BY wc.[Code]
            ORDER BY ms.[starttime] DESC
        ) AS rn
    FROM dbo.[machine_state_vector] ms
    INNER JOIN [{_analyticsSchema}].[DimWorkcenter] wc
        ON wc.[ID] = ms.[wc_id]
    WHERE wc.[Code] IN ({string.Join(", ", parameterNames)})
)
SELECT
    WorkcenterCode,
    CASE availability
        WHEN 1 THEN N'Výroba'
        WHEN 2 THEN N'Prostoj'
        WHEN 3 THEN N'Plánovaná odstávka'
        ELSE N'Neznámý'
    END AS StateText,
    CASE availability
        WHEN 1 THEN 'Production'
        WHEN 2 THEN 'Down'
        WHEN 3 THEN 'Setup'
        ELSE 'Unknown'
    END AS StateCategory,
    CASE availability
        WHEN 1 THEN '#4CAF50'
        WHEN 2 THEN '#F44336'
        WHEN 3 THEN '#FFC107'
        ELSE '#9E9E9E'
    END AS StateColor,
    StateSince
FROM LatestState
WHERE rn = 1;";

        private static string EscapeIdentifier(string value) =>
            value.Replace("]", "]]");

        private static int GetOrdinal(DbDataReader reader, string name)
        {
            for (var i = 0; i < reader.FieldCount; i++)
                if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return i;

            throw new IndexOutOfRangeException(
                $"Column '{name}' was not returned by the SCR10 query.");
        }

        private static string ReadString(
            DbDataReader reader,
            string column,
            string fallback = "")
        {
            var ordinal = GetOrdinal(reader, column);
            return reader.IsDBNull(ordinal)
                ? fallback
                : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? fallback;
        }

        private static decimal ReadDecimal(DbDataReader reader, string column)
        {
            var ordinal = GetOrdinal(reader, column);
            return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static int ReadInt32(DbDataReader reader, string column)
        {
            var ordinal = GetOrdinal(reader, column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static DateTime? ReadNullableDateTime(DbDataReader reader, string column)
        {
            var ordinal = GetOrdinal(reader, column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
        }
    }
}