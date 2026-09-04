using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Models;
using Microsoft.Data.SqlClient;

namespace DMS.Integration.Mes.Reporting;

public sealed class MesReportingDataService
    : IMesReportingDataService
{
    private readonly MesDatabaseConnectionSettings _settings;
    private readonly IMesSqlConnectionFactory _connectionFactory;
    private readonly string _schema;

    public MesReportingDataService(
        MesDatabaseConnectionSettings settings,
        IMesSqlConnectionFactory? connectionFactory = null)
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
    }

    public async Task<IReadOnlyList<MesWorkcenterRecord>> GetWorkcentersAsync(
        CancellationToken cancellationToken = default)
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

        command.CommandText =
            $"""
            SELECT
                ID,
                Code,
                Description,
                ErpCode,
                PlantName
            FROM [{_schema}].[DimWorkcenter]
            ORDER BY Code;
            """;

        var result =
            new List<MesWorkcenterRecord>();

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            result.Add(
                new MesWorkcenterRecord
                {
                    Id = reader.GetGuid(0),
                    Code = GetString(reader, 1),
                    Description = GetString(reader, 2),
                    ErpCode = GetString(reader, 3),
                    PlantName = GetString(reader, 4)
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<MesProductionRecord>> GetProductionAsync(
        MesReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.Normalize();

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

        command.CommandText =
            $"""
            SELECT TOP (@maxRows)
                mes.Starttime,
                mes.Endtime,
                wc.Code,
                wc.Description,
                wc.PlantName,
                op.OrderCode,
                op.OperationCode,
                op.OperationQuantity,
                op.ProductCode,
                op.ProductDescription,
                op.OrderQuantity,
                mes.PerformanceTotal,
                mes.PerformanceGood,
                mes.PerformanceBad,
                mes.PerformanceRework,
                mes.DurationUtilization,
                mes.DurationDown
            FROM [{_schema}].[FactMdaMes] AS mes
            LEFT JOIN [{_schema}].[DimWorkcenter] AS wc
                ON wc.ID = mes.WorkcenterID
            LEFT JOIN [{_schema}].[DimMdaOperation] AS op
                ON op.ID = mes.OperationID
            WHERE
                mes.Starttime < @to
                AND COALESCE(mes.Endtime, @to) >= @from
                AND (@workcenter = N'' OR wc.Code = @workcenter)
                AND (@orderCode = N'' OR op.OrderCode = @orderCode)
                AND (@productCode = N'' OR op.ProductCode = @productCode)
            ORDER BY mes.Starttime DESC;
            """;

        AddCommonParameters(
            command,
            filter);

        var result =
            new List<MesProductionRecord>();

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            result.Add(
                new MesProductionRecord
                {
                    Starttime = reader.GetDateTime(0),
                    Endtime = GetNullableDateTime(reader, 1),
                    WorkcenterCode = GetString(reader, 2),
                    WorkcenterDescription = GetString(reader, 3),
                    PlantName = GetString(reader, 4),
                    OrderCode = GetString(reader, 5),
                    OperationCode = GetString(reader, 6),
                    OperationQuantity = GetNullableDecimal(reader, 7),
                    ProductCode = GetString(reader, 8),
                    ProductDescription = GetString(reader, 9),
                    OrderQuantity = GetNullableDecimal(reader, 10),
                    PerformanceTotal = GetNullableDecimal(reader, 11),
                    PerformanceGood = GetNullableDecimal(reader, 12),
                    PerformanceBad = GetNullableDecimal(reader, 13),
                    PerformanceRework = GetNullableDecimal(reader, 14),
                    DurationUtilization = GetNullableDecimal(reader, 15),
                    DurationDown = GetNullableDecimal(reader, 16)
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<MesStateRecord>> GetStatesAsync(
        MesReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.Normalize();

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

        command.CommandText =
            $"""
            SELECT TOP (@maxRows)
                wc.Code,
                op.OrderCode,
                op.ProductCode,
                stateFact.Starttime,
                stateFact.Endtime,
                stateDim.Name,
                stateDim.Description,
                stateDim.CategoryName,
                stateDim.IsSetup,
                stateDim.IsBreak,
                stateDim.IsCauselessFailure,
                stateFact.CustomText,
                DATEDIFF_BIG(
                    MILLISECOND,
                    CASE
                        WHEN stateFact.Starttime < @from THEN @from
                        ELSE stateFact.Starttime
                    END,
                    CASE
                        WHEN COALESCE(stateFact.Endtime, @effectiveTo) > @effectiveTo THEN @effectiveTo
                        ELSE COALESCE(stateFact.Endtime, @effectiveTo)
                    END
                ) / 1000.0 AS DurationSeconds
            FROM [{_schema}].[FactMdaState] AS stateFact
            LEFT JOIN [{_schema}].[FactMdaMes] AS mes
                ON mes.ID = stateFact.MesID
            LEFT JOIN [{_schema}].[DimMdaState] AS stateDim
                ON stateDim.ID = stateFact.StateID
            LEFT JOIN [{_schema}].[DimWorkcenter] AS wc
                ON wc.ID = mes.WorkcenterID
            LEFT JOIN [{_schema}].[DimMdaOperation] AS op
                ON op.ID = mes.OperationID
            WHERE
                stateFact.Starttime < @effectiveTo
                AND COALESCE(stateFact.Endtime, @effectiveTo) >= @from
                AND (@workcenter = N'' OR wc.Code = @workcenter)
                AND (@orderCode = N'' OR op.OrderCode = @orderCode)
                AND (@productCode = N'' OR op.ProductCode = @productCode)
            ORDER BY stateFact.Starttime DESC;
            """;

        AddCommonParameters(
            command,
            filter);

        var result =
            new List<MesStateRecord>();

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            result.Add(
                new MesStateRecord
                {
                    WorkcenterCode = GetString(reader, 0),
                    OrderCode = GetString(reader, 1),
                    ProductCode = GetString(reader, 2),
                    Starttime = reader.GetDateTime(3),
                    Endtime = GetNullableDateTime(reader, 4),
                    StateName = GetString(reader, 5),
                    StateDescription = GetString(reader, 6),
                    CategoryName = GetString(reader, 7),
                    IsSetup = GetBool(reader, 8),
                    IsBreak = GetBool(reader, 9),
                    IsCauselessFailure = GetBool(reader, 10),
                    CustomText = GetString(reader, 11),
                    DurationSeconds =
                        reader.IsDBNull(12)
                            ? 0d
                            : Convert.ToDouble(
                                reader.GetValue(12))
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<MesCounterRecord>> GetCountersAsync(
        MesReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.Normalize();

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

        command.CommandText =
            $"""
            SELECT TOP (@maxRows)
                wc.Code,
                op.OrderCode,
                op.ProductCode,
                counterDim.Name,
                counterDim.Description,
                counterFact.Timestamp,
                counterFact.Value,
                counterFact.CustomText
            FROM [{_schema}].[FactMdaCounter] AS counterFact
            LEFT JOIN [{_schema}].[FactMdaMes] AS mes
                ON mes.ID = counterFact.MesID
            LEFT JOIN [{_schema}].[DimMdaCounter] AS counterDim
                ON counterDim.ID = counterFact.CounterID
            LEFT JOIN [{_schema}].[DimWorkcenter] AS wc
                ON wc.ID = mes.WorkcenterID
            LEFT JOIN [{_schema}].[DimMdaOperation] AS op
                ON op.ID = mes.OperationID
            WHERE
                counterFact.Timestamp >= @from
                AND counterFact.Timestamp < @to
                AND (@workcenter = N'' OR wc.Code = @workcenter)
                AND (@orderCode = N'' OR op.OrderCode = @orderCode)
                AND (@productCode = N'' OR op.ProductCode = @productCode)
            ORDER BY counterFact.Timestamp DESC;
            """;

        AddCommonParameters(
            command,
            filter);

        var result =
            new List<MesCounterRecord>();

        await using var reader =
            await command
                .ExecuteReaderAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            result.Add(
                new MesCounterRecord
                {
                    WorkcenterCode = GetString(reader, 0),
                    OrderCode = GetString(reader, 1),
                    ProductCode = GetString(reader, 2),
                    CounterName = GetString(reader, 3),
                    CounterDescription = GetString(reader, 4),
                    Timestamp = reader.GetDateTime(5),
                    Value =
                        reader.IsDBNull(6)
                            ? 0m
                            : Convert.ToDecimal(
                                reader.GetValue(6)),
                    CustomText = GetString(reader, 7)
                });
        }

        return result;
    }

    private static void AddCommonParameters(
        SqlCommand command,
        MesReportFilter filter)
    {
        command.Parameters.AddWithValue(
            "@maxRows",
            filter.MaxRows);

        command.Parameters.AddWithValue(
            "@from",
            filter.From);

        command.Parameters.AddWithValue(
            "@to",
            filter.To);

        command.Parameters.AddWithValue(
            "@effectiveTo",
            filter.To < DateTime.Now
                ? filter.To
                : DateTime.Now);

        command.Parameters.AddWithValue(
            "@workcenter",
            filter.WorkcenterCode);

        command.Parameters.AddWithValue(
            "@orderCode",
            filter.OrderCode);

        command.Parameters.AddWithValue(
            "@productCode",
            filter.ProductCode);
    }

    private static string GetString(
        SqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(ordinal))
              ?? string.Empty;

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDateTime(
                reader.GetValue(ordinal));

    private static decimal? GetNullableDecimal(
        SqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(
                reader.GetValue(ordinal));

    private static bool GetBool(
        SqlDataReader reader,
        int ordinal) =>
        !reader.IsDBNull(ordinal)
        && Convert.ToBoolean(
            reader.GetValue(ordinal));
}
