using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
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

        const string sql = """
            WITH RoutingAgg AS
            (
                SELECT
                    production_order_id,
                    MAX(COALESCE(quantity, 0)) AS TargetQuantity,
                    MAX(COALESCE(finished_quantity, 0)) AS FinishedQuantity,
                    MAX(COALESCE(scrap_quantity, 0)) AS ScrapQuantity,
                    MIN(planned_start_date) AS PlannedStart,
                    MAX(planned_end_date) AS PlannedEnd,
                    MIN(actual_start_date) AS ActualStart,
                    MAX(actual_end_date) AS ActualEnd,
                    COUNT_BIG(*) AS OperationCount
                FROM dbo.d_pda_po_routing
                GROUP BY production_order_id
            )
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
                COALESCE(ra.TargetQuantity, 0) AS TargetQuantity,
                COALESCE(ra.FinishedQuantity, 0) AS FinishedQuantity,
                COALESCE(ra.ScrapQuantity, 0) AS ScrapQuantity,
                ra.PlannedStart,
                ra.PlannedEnd,
                ra.ActualStart,
                ra.ActualEnd,
                COALESCE(CONVERT(int, ra.OperationCount), 0) AS OperationCount
            FROM dbo.d_pda_po po
            LEFT JOIN RoutingAgg ra
                ON ra.production_order_id = po.id
            WHERE
                NULLIF(LTRIM(RTRIM(po.code)), '') IS NOT NULL
                AND
                (
                    @search = ''
                    OR po.code LIKE '%' + @search + '%'
                    OR COALESCE(po.product_code, '') LIKE '%' + @search + '%'
                    OR COALESCE(po.product_description, '') LIKE '%' + @search + '%'
                    OR COALESCE(po.cf_customer, '') LIKE '%' + @search + '%'
                )
            ORDER BY
                TRY_CONVERT(bigint, po.code) DESC,
                po.code DESC;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = CreateCommand(connection, sql);
        AddParameter(
            command,
            "@maxRows",
            Math.Clamp(filter.MaxRows, 1, 5000));
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

    public async Task<IReadOnlyList<MesProductionOrderOperationRecord>> GetOperationsAsync(
        Guid productionOrderId,
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId == Guid.Empty)
        {
            return Array.Empty<MesProductionOrderOperationRecord>();
        }

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
                COALESCE(actualWc.WorkcenterCode, '') AS WorkcenterCode
            FROM dbo.d_pda_po_routing r
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
                ORDER BY m.Starttime DESC, m.ID DESC
            ) actualWc
            WHERE r.production_order_id = @productionOrderId
            ORDER BY
                TRY_CONVERT(int, r.code),
                r.code;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

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
