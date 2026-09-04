using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Definitions;
using DMS.Integration.Mes.Reporting.Models;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DMS.Integration.Mes.Reporting;

/// <summary>
/// One currently active FASTEC human-resource assignment.
/// Read-only DTO for MES06.
/// </summary>
public sealed class MesLoggedOperatorRecord
{
    public string WorkcenterCode { get; init; } = string.Empty;
    public string WorkcenterDescription { get; init; } = string.Empty;

    public string HumanCode { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string CostCenter { get; init; } = string.Empty;

    public string Shift { get; init; } = string.Empty;
    public DateTime? LoginFrom { get; init; }
    public bool HasOperator =>
        !string.IsNullOrWhiteSpace(HumanCode);
    public double DurationMinutes { get; init; }

    public string OrderCode { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;

    public string OperatorName
    {
        get
        {
            if (!HasOperator)
            {
                return "—";
            }

            if (!string.IsNullOrWhiteSpace(LastName) &&
                !string.IsNullOrWhiteSpace(FirstName))
            {
                return $"{LastName}, {FirstName}";
            }

            if (!string.IsNullOrWhiteSpace(LastName))
            {
                return LastName;
            }

            if (!string.IsNullOrWhiteSpace(FirstName))
            {
                return FirstName;
            }

            return HumanCode;
        }
    }

    public string DurationText
    {
        get
        {
            if (!LoginFrom.HasValue)
            {
                return string.Empty;
            }

            var totalMinutes =
                Math.Max(
                    0d,
                    DurationMinutes);

            var hours =
                (int)Math.Floor(
                    totalMinutes / 60d);

            var minutes =
                (int)Math.Floor(
                    totalMinutes % 60d);

            return hours > 0
                ? $"{hours} h {minutes:00} min"
                : $"{minutes} min";
        }
    }
}

/// <summary>
/// Adds the MES06 "Logged operators" report without replacing the external report
/// definitions file. The normal JSON definitions remain authoritative; this only
/// appends the report when it is missing.
/// </summary>
public static class MesLoggedOperatorsReportSupport
{
    public const string ReportCode = "LoggedOperators";
    public const string DataSourceCode = "LoggedOperators";

    public static IReadOnlyList<MesReportDefinition> AddDefinitionIfMissing(
        IReadOnlyList<MesReportDefinition>? source)
    {
        var result =
            source?.ToList()
            ?? new List<MesReportDefinition>();

        if (result.Any(item =>
                string.Equals(
                    item.Code,
                    ReportCode,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        result.Add(CreateDefinition());
        return result;
    }

    private static MesReportDefinition CreateDefinition()
    {
        const string json =
            """
            {
              "code": "LoggedOperators",
              "name": "Logged operators",
              "nameKey": "MES06.Report.LoggedOperators.Name",
              "description": "Currently logged FASTEC operators by work center.",
              "descriptionKey": "MES06.Report.LoggedOperators.Description",
              "dataSource": "LoggedOperators",
              "maxRows": 2000,
              "columns": [
                {
                  "property": "WorkcenterCode",
                  "header": "Work center",
                  "headerKey": "MES06.LoggedOperators.Column.Workcenter",
                  "width": 105
                },
                {
                  "property": "Shift",
                  "header": "Shift",
                  "headerKey": "MES06.LoggedOperators.Column.Shift",
                  "width": 100
                },
                {
                  "property": "WorkcenterDescription",
                  "header": "Work center name",
                  "headerKey": "MES06.LoggedOperators.Column.WorkcenterDescription",
                  "width": 180
                },
                {
                  "property": "OperatorName",
                  "header": "Operator",
                  "headerKey": "MES06.LoggedOperators.Column.Operator",
                  "width": 180
                },
                {
                  "property": "HumanCode",
                  "header": "Personnel no.",
                  "headerKey": "MES06.LoggedOperators.Column.HumanCode",
                  "width": 105
                },
                {
                  "property": "LoginFrom",
                  "header": "Logged in from",
                  "headerKey": "MES06.LoggedOperators.Column.LoginFrom",
                  "format": "dd.MM.yyyy HH:mm",
                  "width": 135
                },
                {
                  "property": "DurationText",
                  "header": "Duration",
                  "headerKey": "MES06.LoggedOperators.Column.Duration",
                  "width": 100
                },
                {
                  "property": "OrderCode",
                  "header": "Order",
                  "headerKey": "MES06.LoggedOperators.Column.Order",
                  "width": 105
                },
                {
                  "property": "ProductCode",
                  "header": "Article",
                  "headerKey": "MES06.LoggedOperators.Column.Article",
                  "width": 145
                },
                {
                  "property": "OperationCode",
                  "header": "Operation",
                  "headerKey": "MES06.LoggedOperators.Column.Operation",
                  "width": 105
                },
                {
                  "property": "CostCenter",
                  "header": "Cost center",
                  "headerKey": "MES06.LoggedOperators.Column.CostCenter",
                  "width": 120
                }
              ],
              "chart": null
            }
            """;

        var definition =
            JsonSerializer.Deserialize<MesReportDefinition>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        return definition
            ?? throw new InvalidOperationException(
                "MES06 LoggedOperators report definition could not be created.");
    }

    /// <summary>
    /// Extension used by the existing MES06 LoadRowsAsync dispatch. The current
    /// MesReportingDataService already owns the effective database settings; they
    /// are obtained generically so the existing reporting service/API is not changed.
    /// </summary>
    public static async Task<IReadOnlyList<MesLoggedOperatorRecord>>
    GetLoggedOperatorsAsync(
        this IMesReportingDataService service,
        MesReportFilter filter,
        IReadOnlyList<string> workcenterCodes)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(filter);

        var settings =
            ExtractDatabaseSettings(service);

        var reader =
            new MesLoggedOperatorsDataService(
                settings);

        return await reader
            .GetLoggedOperatorsAsync(
                filter,
                workcenterCodes)
            .ConfigureAwait(false);
    }

    private static MesDatabaseConnectionSettings ExtractDatabaseSettings(
        object service)
    {
        for (var type = service.GetType();
             type is not null;
             type = type.BaseType)
        {
            var fields =
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                if (field.GetValue(service)
                    is MesDatabaseConnectionSettings settings)
                {
                    return settings;
                }
            }

            var properties =
                type.GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic);

            foreach (var property in properties)
            {
                if (!property.CanRead ||
                    property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                try
                {
                    if (property.GetValue(service)
                        is MesDatabaseConnectionSettings settings)
                    {
                        return settings;
                    }
                }
                catch
                {
                    // Ignore diagnostic/private properties that cannot be read.
                }
            }
        }

        throw new InvalidOperationException(
            "MES06 LoggedOperators could not obtain the active MES database settings.");
    }
}

/// <summary>
/// Dedicated read-only query for current personnel assignments.
/// It intentionally uses only SELECT and parameterized values.
/// </summary>
internal sealed class MesLoggedOperatorsDataService
{
    private readonly MesDatabaseConnectionSettings _settings;

    public MesLoggedOperatorsDataService(
        MesDatabaseConnectionSettings settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<MesLoggedOperatorRecord>>
    GetLoggedOperatorsAsync(
        MesReportFilter filter,
        IReadOnlyList<string> workcenterCodes)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var now = DateTime.Now;
        var schema =
            GetSafeSchemaName(_settings);

        var whereWorkcenters =
            BuildWorkcenterPredicate(
                workcenterCodes);

        var sql =
            $"""
            SELECT DISTINCT TOP (@MaxRows)
                wc.Code             AS WorkcenterCode,
                wc.Description      AS WorkcenterDescription,
                ISNULL(sh.Name, '') AS Shift,

                h.HumanCode         AS HumanCode,
                h.FirstName         AS FirstName,
                h.LastName          AS LastName,
                h.CostCenter        AS CostCenter,
                h.Starttime         AS LoginFrom,

                op.OrderCode        AS OrderCode,
                op.OperationCode    AS OperationCode,
                op.ProductCode      AS ProductCode,
                op.ProductDescription AS ProductDescription

            FROM [{schema}].[DimWorkcenter] AS wc

            OUTER APPLY
            (
                SELECT TOP (1)
                    m0.ID,
                    m0.OperationID,
                    m0.ShiftID,
                    m0.Starttime,
                    m0.Endtime
                FROM [{schema}].[FactMdaMes] AS m0
                WHERE
                    m0.WorkcenterID = wc.ID
                    AND m0.Starttime <= @Now
                    AND (
                        m0.Endtime IS NULL
                        OR m0.Endtime > @Now
                    )
                ORDER BY
                    m0.Starttime DESC
            ) AS m

            LEFT JOIN [{schema}].[FactHResLinkExt] AS h
                ON h.MesID = m.ID
                AND h.Starttime <= @Now
                AND (
                    h.Endtime IS NULL
                    OR h.Endtime > @Now
                )

            LEFT JOIN [{schema}].[DimMdaOperation] AS op
                ON op.ID = m.OperationID

            LEFT JOIN [{schema}].[DimShiftEvent] AS sh
                ON sh.ID = m.ShiftID

            WHERE
                1 = 1
                {whereWorkcenters}

                AND (
                    @OrderCode = N''
                    OR op.OrderCode LIKE N'%' + @OrderCode + N'%'
                )

                AND (
                    @ProductCode = N''
                    OR op.ProductCode LIKE N'%' + @ProductCode + N'%'
                )

            ORDER BY
                wc.Code,
                h.LastName,
                h.FirstName,
                h.Starttime;
            """;

        await using var connection =
            CreateSqlConnection();

        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;
        command.CommandTimeout =
            Math.Clamp(
                ReadInt(
                    _settings,
                    30,
                    "CommandTimeout",
                    "CommandTimeoutSeconds",
                    "QueryTimeout"),
                1,
                600);

        AddParameter(
            command,
            "@MaxRows",
            DbType.Int32,
            Math.Clamp(
                ReadInt(
                    filter,
                    2000,
                    "MaxRows"),
                1,
                10000));

        AddParameter(
            command,
            "@Now",
            DbType.DateTime,
            now);

        AddParameter(
            command,
            "@OrderCode",
            DbType.String,
            ReadString(
                filter,
                "OrderCode",
                "Order"));

        AddParameter(
            command,
            "@ProductCode",
            DbType.String,
            ReadString(
                filter,
                "ProductCode",
                "ArticleCode",
                "Product"));

        for (var index = 0;
             index < workcenterCodes.Count;
             index++)
        {
            AddParameter(
                command,
                $"@Workcenter{index}",
                DbType.String,
                workcenterCodes[index]);
        }

        var result =
            new List<MesLoggedOperatorRecord>();

        await using var reader =
            await command
                .ExecuteReaderAsync()
                .ConfigureAwait(false);

        while (await reader
                   .ReadAsync()
                   .ConfigureAwait(false))
        {
            var loginFrom =
                ReadDateTime(
                    reader,
                    "LoginFrom");

            result.Add(
                new MesLoggedOperatorRecord
                {
                    WorkcenterCode =
                        ReadDbString(
                            reader,
                            "WorkcenterCode"),
                    WorkcenterDescription =
                        ReadDbString(
                            reader,
                            "WorkcenterDescription"),
                    HumanCode =
                        ReadDbString(
                            reader,
                            "HumanCode"),
                    FirstName =
                        ReadDbString(
                            reader,
                            "FirstName"),
                    LastName =
                        ReadDbString(
                            reader,
                            "LastName"),
                    CostCenter =
                        ReadDbString(
                            reader,
                            "CostCenter"),
                    Shift =
                        ReadDbString(
                            reader,
                            "Shift"),

                    LoginFrom =
                        loginFrom,

                    DurationMinutes =
                        loginFrom.HasValue
                            ? Math.Max(
                                0d,
                                (now - loginFrom.Value)
                                    .TotalMinutes)
                            : 0d,
                    OrderCode =
                        ReadDbString(
                            reader,
                            "OrderCode"),
                    OperationCode =
                        ReadDbString(
                            reader,
                            "OperationCode"),
                    ProductCode =
                        ReadDbString(
                            reader,
                            "ProductCode"),
                    ProductDescription =
                        ReadDbString(
                            reader,
                            "ProductDescription")
                });
        }

        return result;
    }

    private DbConnection CreateSqlConnection()
    {
        var connectionString =
            ReadString(
                _settings,
                "ConnectionString");

        if (string.IsNullOrWhiteSpace(
                connectionString))
        {
            connectionString =
                BuildConnectionString();
        }

        var connectionType =
            ResolveSqlConnectionType();

        var instance =
            Activator.CreateInstance(
                connectionType,
                connectionString);

        return instance as DbConnection
            ?? throw new InvalidOperationException(
                $"SQL provider {connectionType.FullName} is not a DbConnection.");
    }

    private string BuildConnectionString()
    {
        var server =
            ReadString(
                _settings,
                "Server",
                "DataSource",
                "SqlServer");

        var database =
            ReadString(
                _settings,
                "Database",
                "InitialCatalog",
                "Catalog");

        if (string.IsNullOrWhiteSpace(server) ||
            string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "MES06 LoggedOperators: MES SQL server/database settings are incomplete.");
        }

        var user =
            ReadString(
                _settings,
                "UserName",
                "Username",
                "UserId",
                "UserID");

        var password =
            ReadString(
                _settings,
                "Password");

        var builder =
            new DbConnectionStringBuilder
            {
                ["Data Source"] = server,
                ["Initial Catalog"] = database,
                ["Encrypt"] =
                    ReadBool(
                        _settings,
                        true,
                        "Encrypt"),
                ["TrustServerCertificate"] =
                    ReadBool(
                        _settings,
                        true,
                        "TrustServerCertificate"),
                ["Connect Timeout"] =
                    Math.Clamp(
                        ReadInt(
                            _settings,
                            15,
                            "ConnectTimeout",
                            "ConnectionTimeout",
                            "ConnectTimeoutSeconds"),
                        1,
                        120),
                ["Application Name"] =
                    "DMS MES06 LoggedOperators"
            };

        if (!string.IsNullOrWhiteSpace(user))
        {
            builder["Integrated Security"] = false;
            builder["User ID"] = user;
            builder["Password"] = password;
        }
        else
        {
            builder["Integrated Security"] = true;
        }

        return builder.ConnectionString;
    }

    private static Type ResolveSqlConnectionType()
    {
        var candidates =
            new[]
            {
                (
                    Assembly: "Microsoft.Data.SqlClient",
                    Type: "Microsoft.Data.SqlClient.SqlConnection"
                ),
                (
                    Assembly: "System.Data.SqlClient",
                    Type: "System.Data.SqlClient.SqlConnection"
                )
            };

        foreach (var candidate
                 in candidates)
        {
            var qualified =
                $"{candidate.Type}, {candidate.Assembly}";

            var type =
                Type.GetType(
                    qualified,
                    throwOnError: false);

            if (type is not null &&
                typeof(DbConnection)
                    .IsAssignableFrom(type))
            {
                return type;
            }

            try
            {
                var assembly =
                    Assembly.Load(
                        candidate.Assembly);

                type =
                    assembly.GetType(
                        candidate.Type,
                        throwOnError: false);

                if (type is not null &&
                    typeof(DbConnection)
                        .IsAssignableFrom(type))
                {
                    return type;
                }
            }
            catch
            {
                // Try the next SQL provider.
            }
        }

        throw new InvalidOperationException(
            "MES06 LoggedOperators could not locate Microsoft.Data.SqlClient or System.Data.SqlClient. " +
            "The normal MES reporting SQL provider must be available.");
    }

    private static string BuildWorkcenterPredicate(
        IReadOnlyList<string> workcenterCodes)
    {
        if (workcenterCodes.Count == 0)
        {
            return string.Empty;
        }

        var parameters =
            Enumerable.Range(
                    0,
                    workcenterCodes.Count)
                .Select(index =>
                    $"@Workcenter{index}");

        return
            $"AND wc.Code IN ({string.Join(", ", parameters)})";
    }

    private static IReadOnlyList<string> ReadWorkcenterCodes(
        object filter)
    {
        var values =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var type =
            filter.GetType();

        // Supports the original single-workcenter filter.
        AddIfNotBlank(
            values,
            ReadString(
                filter,
                "WorkcenterCode"));

        // Supports current/future multi-workcenter variants without taking a
        // compile-time dependency on their exact property name.
        foreach (var property
                 in type.GetProperties(
                     BindingFlags.Instance
                     | BindingFlags.Public))
        {
            if (!property.Name.Contains(
                    "Workcenter",
                    StringComparison.OrdinalIgnoreCase) ||
                property.PropertyType == typeof(string) ||
                !typeof(IEnumerable)
                    .IsAssignableFrom(
                        property.PropertyType) ||
                !property.CanRead)
            {
                continue;
            }

            object? raw;

            try
            {
                raw =
                    property.GetValue(
                        filter);
            }
            catch
            {
                continue;
            }

            if (raw is not IEnumerable items)
            {
                continue;
            }

            foreach (var item in items)
            {
                if (item is null)
                {
                    continue;
                }

                if (item is string text)
                {
                    AddIfNotBlank(
                        values,
                        text);
                    continue;
                }

                AddIfNotBlank(
                    values,
                    ReadString(
                        item,
                        "Code",
                        "WorkcenterCode",
                        "Value"));
            }
        }

        return values
            .OrderBy(
                value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddIfNotBlank(
        ISet<string> target,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(
                value.Trim());
        }
    }

    private static string GetSafeSchemaName(
        object settings)
    {
        var schema =
            ReadString(
                settings,
                "Schema",
                "ReportingSchema");

        if (string.IsNullOrWhiteSpace(schema))
        {
            return "ana";
        }

        schema =
            schema.Trim();

        return Regex.IsMatch(
            schema,
            @"^[A-Za-z0-9_]+$")
            ? schema
            : "ana";
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object? value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value =
            value
            ?? DBNull.Value;

        command.Parameters.Add(
            parameter);
    }

    private static string ReadDbString(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(ordinal))
              ?? string.Empty;
    }

    private static DateTime? ReadDateTime(
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

    private static string ReadString(
        object source,
        params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var property =
                source.GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.IgnoreCase);

            if (property is null ||
                !property.CanRead ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                var value =
                    property.GetValue(
                        source);

                if (value is not null)
                {
                    var text =
                        Convert.ToString(value)
                        ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
            catch
            {
                // Ignore optional properties that cannot be read.
            }
        }

        return string.Empty;
    }

    private static int ReadInt(
        object source,
        int fallback,
        params string[] propertyNames)
    {
        var raw =
            ReadString(
                source,
                propertyNames);

        return int.TryParse(
            raw,
            out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(
        object source,
        bool fallback,
        params string[] propertyNames)
    {
        var raw =
            ReadString(
                source,
                propertyNames);

        return bool.TryParse(
            raw,
            out var value)
            ? value
            : fallback;
    }
}
