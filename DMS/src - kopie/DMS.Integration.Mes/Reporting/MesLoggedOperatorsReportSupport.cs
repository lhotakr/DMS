using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting;

/// <summary>
/// One FASTEC human-resource assignment in the selected MES06 interval.
/// A row can also represent a work center / MES interval without a logged operator.
/// </summary>
public sealed class MesLoggedOperatorRecord
{
    public string WorkcenterCode { get; init; } = string.Empty;
    public string WorkcenterDescription { get; init; } = string.Empty;

    public string Shift { get; init; } = string.Empty;

    public string HumanCode { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string CostCenter { get; init; } = string.Empty;

    public DateTime? LoginFrom { get; init; }
    public DateTime? LoginTo { get; init; }
    public double DurationMinutes { get; init; }
    public string SapNumber { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductDescription { get; init; } = string.Empty;

    public bool HasOperator =>
        !string.IsNullOrWhiteSpace(HumanCode);

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
/// MES06 read-only operator-history support.
/// The report definition itself is intentionally NOT created here; it belongs
/// to the shared external MES06 report-definition JSON.
/// </summary>
public static class MesLoggedOperatorsReportSupport
{
    public const string ReportCode = "LoggedOperators";
    public const string DataSourceCode = "LoggedOperators";

    public static async Task<IReadOnlyList<MesLoggedOperatorRecord>>
    GetLoggedOperatorsAsync(
        this IMesReportingDataService service,
        MesReportFilter filter,
        IReadOnlyList<string> workcenterCodes,
        string shiftCode,
        string operationCode)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(filter);

        var settings =
            ExtractDatabaseSettings(
                service);

        var reader =
            new MesLoggedOperatorsDataService(
                settings);

        return await reader
            .GetLoggedOperatorsAsync(
                filter,
                workcenterCodes,
                shiftCode,
                operationCode)
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
/// Dedicated read-only historical query for FASTEC personnel assignments.
/// Uses only SELECT and parameterized values.
/// </summary>
internal sealed class MesLoggedOperatorsDataService
{
    private readonly MesDatabaseConnectionSettings _settings;

    public MesLoggedOperatorsDataService(
        MesDatabaseConnectionSettings settings)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));
    }

    private static IReadOnlyList<MesLoggedOperatorRecord> AggregateRows(
    IReadOnlyList<MesLoggedOperatorRecord> rows)
    {
        return rows
            .GroupBy(row => new
            {
                row.Shift,
                row.WorkcenterCode,
                row.WorkcenterDescription,
                row.HumanCode,
                row.FirstName,
                row.LastName,
                row.OrderCode,
                row.ProductCode,
                row.SapNumber,
                row.OperationCode
            })
            .Select(group => new MesLoggedOperatorRecord
            {
                Shift =
                    group.Key.Shift,

                WorkcenterCode =
                    group.Key.WorkcenterCode,

                WorkcenterDescription =
                    group.Key.WorkcenterDescription,

                HumanCode =
                    group.Key.HumanCode,

                FirstName =
                    group.Key.FirstName,

                LastName =
                    group.Key.LastName,

                LoginFrom =
                    group.Min(row =>
                        row.LoginFrom),

                LoginTo =
                    group.Max(row =>
                        row.LoginTo),

                DurationMinutes =
                    group.Sum(row =>
                        row.DurationMinutes),

                OrderCode =
                    group.Key.OrderCode,

                ProductCode =
                    group.Key.ProductCode,

                SapNumber =
                    group.Key.SapNumber,

                OperationCode =
                    group.Key.OperationCode
            })
            .OrderBy(row =>
                row.Shift)
            .ThenBy(row =>
                row.WorkcenterCode)
            .ThenBy(row =>
                row.LastName)
            .ThenBy(row =>
                row.FirstName)
            .ToList();
    }

    public async Task<IReadOnlyList<MesLoggedOperatorRecord>>
    GetLoggedOperatorsAsync(
        MesReportFilter filter,
        IReadOnlyList<string> workcenterCodes,
        string shiftCode,
        string operationCode)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.To <= filter.From)
        {
            return Array.Empty<MesLoggedOperatorRecord>();
        }

        var normalizedWorkcenters =
            NormalizeWorkcenterCodes(
                workcenterCodes,
                filter);

        var schema =
            GetSafeSchemaName(
                _settings);

        var whereWorkcenters =
            BuildWorkcenterPredicate(
                normalizedWorkcenters);

        var sql =
            $"""
            SELECT DISTINCT TOP (@MaxRows)
                wc.Code                    AS WorkcenterCode,
                wc.Description             AS WorkcenterDescription,
                ISNULL(sh.Name, N'')       AS Shift,

                h.HumanCode                AS HumanCode,
                h.FirstName                AS FirstName,
                h.LastName                 AS LastName,
                h.CostCenter               AS CostCenter,
                h.Starttime                AS LoginFrom,
                h.Endtime                  AS LoginTo,

                op.OrderCode               AS OrderCode,
                op.OperationCode           AS OperationCode,
                op.ProductCode             AS ProductCode,
                op.ProductDescription      AS ProductDescription

            FROM [{schema}].[DimWorkcenter] AS wc

            LEFT JOIN [{schema}].[FactMdaMes] AS m
                ON m.WorkcenterID = wc.ID
                AND m.Starttime < @To
                AND (
                    m.Endtime IS NULL
                    OR m.Endtime > @From
                )

            LEFT JOIN [{schema}].[FactHResLinkExt] AS h
                ON h.MesID = m.ID
                AND h.Starttime < @To
                AND (
                    h.Endtime IS NULL
                    OR h.Endtime > @From
                )

            LEFT JOIN [{schema}].[DimMdaOperation] AS op
                ON op.ID = m.OperationID

            LEFT JOIN [{schema}].[DimShiftEvent] AS sh
                ON sh.ID = m.ShiftID

            WHERE
                1 = 1
                {whereWorkcenters}

                AND (
                    @ShiftCode = N''
                    OR sh.Name = @ShiftCode
                )

                AND (
                    @OrderCode = N''
                    OR op.OrderCode LIKE N'%' + @OrderCode + N'%'
                )

                AND (
                    @OperationCode = N''
                    OR op.OperationCode LIKE N'%' + @OperationCode + N'%'
                )

                AND (
                    @ProductCode = N''
                    OR op.ProductCode LIKE N'%' + @ProductCode + N'%'
                )

            ORDER BY
                WorkcenterCode,
                Shift,
                LoginFrom,
                LastName,
            FirstName;
            """;

        await using var connection =
            CreateSqlConnection();

        await connection
            .OpenAsync()
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            sql;

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
                    10000,
                    "MaxRows"),
                1,
                50000));

        AddParameter(
            command,
            "@From",
            DbType.DateTime,
            filter.From);

        AddParameter(
            command,
            "@To",
            DbType.DateTime,
            filter.To);

        AddParameter(
            command,
            "@ShiftCode",
            DbType.String,
            shiftCode?.Trim()
            ?? string.Empty);

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
            "@OperationCode",
            DbType.String,
            operationCode?.Trim()
            ?? string.Empty);

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
             index < normalizedWorkcenters.Count;
             index++)
        {
            AddParameter(
                command,
                $"@Workcenter{index}",
                DbType.String,
                normalizedWorkcenters[index]);
        }

        var result =
            new List<MesLoggedOperatorRecord>();

        await using var reader =
            await command
                .ExecuteReaderAsync()
                .ConfigureAwait(false);

        var now =
            DateTime.Now;

        while (await reader
                   .ReadAsync()
                   .ConfigureAwait(false))
        {
            var loginFrom =
                ReadDateTime(
                    reader,
                    "LoginFrom");

            var loginTo =
                ReadDateTime(
                    reader,
                    "LoginTo");

            var durationMinutes =
                CalculateDurationMinutes(
                    loginFrom,
                    loginTo,
                    filter.From,
                    filter.To,
                    now);

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
                    Shift =
                        ReadDbString(
                            reader,
                            "Shift"),

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

                    LoginFrom =
                        loginFrom,
                    LoginTo =
                        loginTo,
                    DurationMinutes =
                        durationMinutes,

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

        //return result;
        return AggregateRows(result);
    }

    private static double CalculateDurationMinutes(
        DateTime? loginFrom,
        DateTime? loginTo,
        DateTime reportFrom,
        DateTime reportTo,
        DateTime now)
    {
        if (!loginFrom.HasValue)
        {
            return 0d;
        }

        var effectiveFrom =
            loginFrom.Value > reportFrom
                ? loginFrom.Value
                : reportFrom;

        var effectiveTo =
            loginTo.HasValue &&
            loginTo.Value < reportTo
                ? loginTo.Value
                : reportTo;

        // An open assignment has no factual end yet. Do not display future
        // duration when the selected MES06 interval extends beyond "now".
        if (!loginTo.HasValue &&
            now < effectiveTo)
        {
            effectiveTo =
                now;
        }

        return Math.Max(
            0d,
            (effectiveTo - effectiveFrom)
                .TotalMinutes);
    }

    private static IReadOnlyList<string> NormalizeWorkcenterCodes(
        IReadOnlyList<string>? workcenterCodes,
        MesReportFilter filter)
    {
        var values =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (workcenterCodes is not null)
        {
            foreach (var value in workcenterCodes)
            {
                AddIfNotBlank(
                    values,
                    value);
            }
        }

        if (values.Count == 0)
        {
            AddIfNotBlank(
                values,
                ReadString(
                    filter,
                    "WorkcenterCode"));
        }

        return values
            .OrderBy(
                value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                    "DMS MES06 LoggedOperators History"
            };

        if (!string.IsNullOrWhiteSpace(user))
        {
            builder["Integrated Security"] =
                false;
            builder["User ID"] =
                user;
            builder["Password"] =
                password;
        }
        else
        {
            builder["Integrated Security"] =
                true;
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

        foreach (var candidate in candidates)
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
                .Select(
                    index =>
                        $"@Workcenter{index}");

        return
            $"AND wc.Code IN ({string.Join(", ", parameters)})";
    }

    private static void AddIfNotBlank(
        ISet<string> target,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(
                value))
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

        if (string.IsNullOrWhiteSpace(
                schema))
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

        parameter.ParameterName =
            name;
        parameter.DbType =
            type;
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

    private static DateTime? ReadDateTime(
        DbDataReader reader,
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
            Convert.ToString(
                value),
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
                        Convert.ToString(
                            value)
                        ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(
                            text))
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
