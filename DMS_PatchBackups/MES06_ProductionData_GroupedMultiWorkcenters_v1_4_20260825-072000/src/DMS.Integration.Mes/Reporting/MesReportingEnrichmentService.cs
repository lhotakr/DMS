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

public sealed class MesReportingShiftEvent
{
    public Guid? Id { get; init; }
    public DateTime Starttime { get; init; }
    public DateTime Endtime { get; init; }
    public string Name { get; init; } = string.Empty;
}
