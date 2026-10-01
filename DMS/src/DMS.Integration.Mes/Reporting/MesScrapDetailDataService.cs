using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace DMS.Integration.Mes.Reporting;

/// <summary>
/// Dedicated read-only service for the long-range SCRAP_DETAIL report.
/// It intentionally avoids the normal MES06 10k counter limit and filters
/// selected counters directly in SQL.
/// </summary>
public sealed class MesScrapDetailDataService
{
    private readonly MesDatabaseConnectionSettings _settings;
    private readonly MesSqlConnectionFactory _connectionFactory = new();
    private readonly string _schema;

    public MesScrapDetailDataService(
        MesDatabaseConnectionSettings settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));

        _settings.Normalize();

        _schema =
            string.IsNullOrWhiteSpace(
                _settings.ReportingSchema)
                ? "ana"
                : _settings.ReportingSchema.Trim();
    }

    public async Task<IReadOnlyList<Mes06CounterCatalogItem>> GetCounterCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _connectionFactory.OpenAsync(
                _settings,
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            Math.Max(
                _settings.CommandTimeoutSeconds,
                60);

        command.CommandText =
            $"""
            SELECT DISTINCT
                COALESCE(
                    NULLIF(LTRIM(RTRIM(dc.[Kind])), ''),
                    'Ostatní') AS GroupName,
                dc.[Name] AS CounterName,
                dc.[Description] AS CounterDescription
            FROM [{_schema}].[DimMdaCounter] dc
            WHERE NULLIF(LTRIM(RTRIM(dc.[Name])), '') IS NOT NULL
            ORDER BY
                GroupName,
                CounterName;
            """;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var result =
            new List<Mes06CounterCatalogItem>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new Mes06CounterCatalogItem
                {
                    GroupName =
                        GetString(
                            reader,
                            "GroupName"),
                    Name =
                        GetString(
                            reader,
                            "CounterName"),
                    Description =
                        GetString(
                            reader,
                            "CounterDescription")
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<Mes06ScrapDetailRecord>> GetReportAsync(
        DateTime from,
        DateTime to,
        IReadOnlyList<string> workcenterCodes,
        string orderCode,
        string operationCode,
        string productCode,
        IReadOnlyList<string> counterNames,
        int maxRows = 100000,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            to =
                from.AddDays(1);
        }

        var workcenters =
            (workcenterCodes
             ?? Array.Empty<string>())
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Select(value =>
                    value.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (workcenters.Count == 0)
        {
            return Array.Empty<Mes06ScrapDetailRecord>();
        }

        var counters =
            (counterNames
             ?? Array.Empty<string>())
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Select(value =>
                    value.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();

        await using var connection =
            await _connectionFactory.OpenAsync(
                _settings,
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            Math.Max(
                _settings.CommandTimeoutSeconds,
                180);

        var wcParameters =
            AddStringParameters(
                command,
                "wc",
                workcenters);

        var counterParameters =
            AddStringParameters(
                command,
                "counter",
                counters);

        var counterClause =
            counterParameters.Count == 0
                ? string.Empty
                : $"AND dc.[Name] IN ({string.Join(", ", counterParameters)})";

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
                op.[ProductDescription],
                dc.[Name] AS CounterName,
                c.[Value],
                c.[CustomText]
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
              AND wc.[Code] IN ({string.Join(", ", wcParameters)})
              {counterClause}
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
                sh.[Starttime],
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
                100000));

        var rawRows =
            new List<ScrapRawRow>();

        await using (var reader =
                     await command.ExecuteReaderAsync(
                         cancellationToken))
        {
            while (await reader.ReadAsync(
                       cancellationToken))
            {
                var timestamp =
                    GetDateTime(
                        reader,
                        "Timestamp");

                var mesId =
                    GetGuid(
                        reader,
                        "MesID");

                if (!timestamp.HasValue
                    || !mesId.HasValue)
                {
                    continue;
                }

                rawRows.Add(
                    new ScrapRawRow
                    {
                        MesId = mesId.Value,
                        Timestamp = timestamp.Value,
                        ShiftName = GetString(reader, "ShiftName"),
                        ShiftStart = GetDateTime(reader, "ShiftStart"),
                        WorkcenterCode = GetString(reader, "WorkcenterCode"),
                        OrderCode = GetString(reader, "OrderCode"),
                        ProductCode = GetString(reader, "ProductCode"),
                        ProductDescription = GetString(reader, "ProductDescription"),
                        CounterName = GetString(reader, "CounterName"),
                        Value = GetDecimal(reader, "Value"),
                        CustomText = GetString(reader, "CustomText")
                    });
            }
        }

        var personnelByMes =
            await LoadPersonnelAsync(
                connection,
                rawRows,
                cancellationToken);

        var totalsByMes =
            rawRows
                .GroupBy(row =>
                    row.MesId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(row =>
                        row.Value
                        ?? 0m));

        return rawRows
            .Select(row =>
            {
                var total =
                    totalsByMes.TryGetValue(
                        row.MesId,
                        out var resolvedTotal)
                        ? resolvedTotal
                        : 0m;

                var itemDesignation =
                    string.IsNullOrWhiteSpace(
                        row.ProductDescription)
                        ? row.ProductCode
                        : row.ProductDescription;

                return new Mes06ScrapDetailRecord
                {
                    MesId = row.MesId,
                    PointInTime =
                        (
                            row.ShiftStart
                            ?? row.Timestamp
                        ).Date,
                    WorkcenterCode = row.WorkcenterCode,
                    Item = row.ProductCode,
                    ItemDesignation = itemDesignation,
                    OrderCode = row.OrderCode,
                    // In the supplied FASTEC workbook this column contains
                    // values such as "Ranní směna" / "Odpolední směna".
                    ItemGroup = row.ShiftName,
                    Personnel =
                        personnelByMes.TryGetValue(
                            row.MesId,
                            out var personnel)
                            ? personnel
                            : string.Empty,
                    CounterName = row.CounterName,
                    Amount = row.Value,
                    Unit = "ks",
                    Percental =
                        row.Value.HasValue
                        && total != 0m
                            ? row.Value.Value / total
                            : null,
                    UserText = row.CustomText
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadPersonnelAsync(
        SqlConnection connection,
        IReadOnlyList<ScrapRawRow> rows,
        CancellationToken cancellationToken)
    {
        var ids =
            rows
                .Select(row => row.MesId)
                .Distinct()
                .ToList();

        var result =
            new Dictionary<Guid, HashSet<string>>();

        const int batchSize = 300;

        for (var offset = 0;
             offset < ids.Count;
             offset += batchSize)
        {
            var batch =
                ids
                    .Skip(offset)
                    .Take(batchSize)
                    .ToList();

            await using var command =
                connection.CreateCommand();

            command.CommandTimeout =
                Math.Max(
                    _settings.CommandTimeoutSeconds,
                    120);

            var parameters =
                new List<string>();

            for (var index = 0;
                 index < batch.Count;
                 index++)
            {
                var name =
                    $"@mes{index}";

                parameters.Add(
                    name);

                AddParameter(
                    command,
                    name,
                    batch[index]);
            }

            command.CommandText =
                $"""
                SELECT
                    h.[MesID],
                    h.[HumanCode]
                FROM [{_schema}].[FactHResLinkExt] h
                WHERE h.[MesID] IN ({string.Join(", ", parameters)})
                  AND NULLIF(LTRIM(RTRIM(h.[HumanCode])), '') IS NOT NULL
                ORDER BY
                    h.[MesID],
                    h.[HumanCode];
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

                var humanCode =
                    GetString(
                        reader,
                        "HumanCode");

                if (!mesId.HasValue
                    || string.IsNullOrWhiteSpace(
                        humanCode))
                {
                    continue;
                }

                if (!result.TryGetValue(
                        mesId.Value,
                        out var people))
                {
                    people =
                        new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase);

                    result[mesId.Value] =
                        people;
                }

                people.Add(
                    humanCode.Trim());
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => string.Join(
                "; ",
                pair.Value.OrderBy(
                    value => value,
                    StringComparer.CurrentCultureIgnoreCase)));
    }

    private static IReadOnlyList<string> AddStringParameters(
        SqlCommand command,
        string prefix,
        IReadOnlyList<string> values)
    {
        var names =
            new List<string>();

        for (var index = 0;
             index < values.Count;
             index++)
        {
            var name =
                $"@{prefix}{index}";

            names.Add(
                name);

            AddParameter(
                command,
                name,
                values[index]);
        }

        return names;
    }

    private static void AddParameter(
        SqlCommand command,
        string name,
        object? value)
    {
        var parameter =
            command.Parameters.AddWithValue(
                name,
                value
                ?? DBNull.Value);

        if (value is string)
        {
            parameter.SqlDbType =
                SqlDbType.NVarChar;
        }
    }

    private static string GetString(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        return reader.IsDBNull(
                   ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(
                      ordinal))
              ?? string.Empty;
    }

    private static DateTime? GetDateTime(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }

        var value =
            reader.GetValue(
                ordinal);

        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        return DateTime.TryParse(
            Convert.ToString(value),
            out var parsed)
            ? parsed
            : null;
    }

    private static Guid? GetGuid(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

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
            Convert.ToString(value),
            out var parsed)
            ? parsed
            : null;
    }

    private static decimal? GetDecimal(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

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

    private sealed class ScrapRawRow
    {
        public Guid MesId { get; init; }
        public DateTime Timestamp { get; init; }
        public DateTime? ShiftStart { get; init; }
        public string ShiftName { get; init; } = string.Empty;
        public string WorkcenterCode { get; init; } = string.Empty;
        public string OrderCode { get; init; } = string.Empty;
        public string ProductCode { get; init; } = string.Empty;
        public string ProductDescription { get; init; } = string.Empty;
        public string CounterName { get; init; } = string.Empty;
        public decimal? Value { get; init; }
        public string CustomText { get; init; } = string.Empty;
    }
}
