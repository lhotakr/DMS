using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Live;

/// <summary>
/// Read-only live overview over the FASTEC analytical SQL schema.
/// The live screen deliberately reads only open MES intervals with an assigned shift,
/// so inactive workcenters are not shown.
/// </summary>
public sealed class MesLiveOverviewDataService
{
    private static readonly Regex SafeSqlIdentifier = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly object _settings;
    private readonly string _schema;
    private readonly int _commandTimeoutSeconds;

    public MesLiveOverviewDataService(object settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _schema = ReadString("ReportingSchema", "Schema", "SchemaName") ?? "ana";
        _commandTimeoutSeconds = Math.Max(5, ReadInt(30, "CommandTimeoutSeconds", "SqlCommandTimeoutSeconds", "CommandTimeout"));

        if (!SafeSqlIdentifier.IsMatch(_schema))
        {
            throw new InvalidOperationException($"Invalid MES reporting schema: '{_schema}'.");
        }
    }

    /// <summary>
    /// Returns workcenter groups available in the current FASTEC analytical snapshot.
    /// The known ana schema does not expose a dedicated workcenter-group dimension;
    /// DimWorkcenterExt.CostCenter is therefore used as the stable group field.
    /// Only groups containing an active workcenter with a current ShiftID are returned.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetWorkcenterGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var sql = $"""
            WITH OpenMesRanked AS
            (
                SELECT
                    m.WorkcenterID,
                    m.ShiftID,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY m.WorkcenterID
                        ORDER BY m.Starttime DESC, m.ID
                    ) AS rn
                FROM [{_schema}].[FactMdaMes] m
                WHERE m.Endtime IS NULL
                  AND m.ShiftID IS NOT NULL
            ),
            ActiveWorkcenters AS
            (
                SELECT WorkcenterID
                FROM OpenMesRanked
                WHERE rn = 1
            )
            SELECT DISTINCT
                LTRIM(RTRIM(wx.CostCenter)) AS GroupName
            FROM ActiveWorkcenters a
            INNER JOIN [{_schema}].[DimWorkcenterExt] wx ON wx.ID = a.WorkcenterID
            WHERE NULLIF(LTRIM(RTRIM(wx.CostCenter)), '') IS NOT NULL
            ORDER BY GroupName;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = GetString(reader, "GroupName").Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }

        return result
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns only active workcenters (open FactMdaMes interval + current ShiftID).
    /// The operator-facing designation is mapped from ErpCode in the available ana schema.
    /// </summary>
    public async Task<IReadOnlyList<MesLiveWorkcenterRecord>> GetWorkcentersAsync(
        string? workcenterGroup = null,
        CancellationToken cancellationToken = default)
    {
        var sql = $"""
            WITH OpenMesRanked AS
            (
                SELECT
                    m.WorkcenterID,
                    m.ShiftID,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY m.WorkcenterID
                        ORDER BY m.Starttime DESC, m.ID
                    ) AS rn
                FROM [{_schema}].[FactMdaMes] m
                WHERE m.Endtime IS NULL
                  AND m.ShiftID IS NOT NULL
            ),
            ActiveWorkcenters AS
            (
                SELECT WorkcenterID
                FROM OpenMesRanked
                WHERE rn = 1
            )
            SELECT
                w.ID,
                w.Code,
                w.Description,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(wx.ErpCode)), ''),
                    NULLIF(LTRIM(RTRIM(w.ErpCode)), ''),
                    w.Code) AS Designation,
                COALESCE(wx.CostCenter, '') AS GroupName
            FROM ActiveWorkcenters a
            INNER JOIN [{_schema}].[DimWorkcenter] w ON w.ID = a.WorkcenterID
            LEFT JOIN [{_schema}].[DimWorkcenterExt] wx ON wx.ID = w.ID
            WHERE NULLIF(LTRIM(RTRIM(w.Code)), '') IS NOT NULL
              AND (@workcenterGroup IS NULL OR wx.CostCenter = @workcenterGroup)
            ORDER BY
                COALESCE(
                    NULLIF(LTRIM(RTRIM(wx.ErpCode)), ''),
                    NULLIF(LTRIM(RTRIM(w.ErpCode)), ''),
                    w.Code),
                w.Code;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddParameter(
            command,
            "@workcenterGroup",
            string.IsNullOrWhiteSpace(workcenterGroup)
                ? null
                : workcenterGroup.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<MesLiveWorkcenterRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MesLiveWorkcenterRecord
            {
                Id = GetGuid(reader, "ID") ?? Guid.Empty,
                Code = GetString(reader, "Code"),
                Description = GetString(reader, "Description"),
                Designation = GetString(reader, "Designation"),
                GroupName = GetString(reader, "GroupName")
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<MesMachineOverviewRecord>> GetOverviewAsync(
        MesMachineOverviewFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var requestedWorkcenters = filter.WorkcenterCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Backward compatibility for callers that still populate only WorkcenterCode.
        if (requestedWorkcenters.Count == 0 &&
            !string.IsNullOrWhiteSpace(filter.WorkcenterCode))
        {
            requestedWorkcenters.Add(filter.WorkcenterCode.Trim());
        }

        var workcenterParameterNames = requestedWorkcenters
            .Select((_, index) => $"@workcenter{index}")
            .ToList();

        var workcenterPredicate = workcenterParameterNames.Count == 0
            ? string.Empty
            : $" AND w.Code IN ({string.Join(", ", workcenterParameterNames)})";

        var sql = $"""
            WITH OpenMesRanked AS
            (
                SELECT
                    m.*,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY m.WorkcenterID
                        ORDER BY m.Starttime DESC, m.ID
                    ) AS rn
                FROM [{_schema}].[FactMdaMes] m
                WHERE m.Endtime IS NULL
                  AND m.ShiftID IS NOT NULL
            ),
            OpenMes AS
            (
                SELECT *
                FROM OpenMesRanked
                WHERE rn = 1
            ),
            CurrentOperations AS
            (
                SELECT DISTINCT OperationID
                FROM OpenMes
                WHERE OperationID IS NOT NULL
            ),
            OrderGood AS
            (
                SELECT
                    m.OperationID,
                    SUM(COALESCE(m.PerformanceGood, 0)) AS GoodAmount
                FROM [{_schema}].[FactMdaMes] m
                INNER JOIN CurrentOperations co ON co.OperationID = m.OperationID
                GROUP BY m.OperationID
            ),
            CurrentShiftKeys AS
            (
                SELECT DISTINCT WorkcenterID, ShiftID
                FROM OpenMes
            ),
            ShiftGood AS
            (
                SELECT
                    m.WorkcenterID,
                    m.ShiftID,
                    SUM(COALESCE(m.PerformanceGood, 0)) AS GoodAmount
                FROM [{_schema}].[FactMdaMes] m
                INNER JOIN CurrentShiftKeys sk
                    ON sk.WorkcenterID = m.WorkcenterID
                   AND sk.ShiftID = m.ShiftID
                GROUP BY m.WorkcenterID, m.ShiftID
            )
            SELECT TOP (@maxRows)
                w.ID AS WorkcenterID,
                w.Code AS WorkcenterCode,
                w.Description AS WorkcenterDescription,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(wx.ErpCode)), ''),
                    NULLIF(LTRIM(RTRIM(w.ErpCode)), ''),
                    w.Code) AS WorkcenterDesignation,
                COALESCE(wx.CostCenter, '') AS WorkcenterGroup,
                m.ShiftID,
                sh.Name AS ShiftName,
                sh.Starttime AS ShiftStarttime,
                sh.Endtime AS ShiftEndtime,
                m.OperationID,
                op.OrderCode,
                op.ProductCode,
                op.ProductDescription,
                os.StateName,
                os.CategoryName AS StateCategory,
                os.Color AS StateColor,
                os.CategoryColor AS StateCategoryColor,
                os.StateStarttime,
                os.StateEndtime,
                os.CustomText AS StateUserText,
                CASE
                    WHEN op.ProcessingTime IS NOT NULL AND op.ProcessingTime > 0
                    THEN CAST(60.0 / op.ProcessingTime AS decimal(18, 3))
                    ELSE NULL
                END AS PlannedPerformancePerMinute,
                CASE
                    WHEN m.WcProcessingTime IS NOT NULL AND m.WcProcessingTime > 0
                    THEN CAST(60.0 / m.WcProcessingTime AS decimal(18, 3))
                    ELSE NULL
                END AS CurrentPerformancePerMinute,
                op.OrderQuantity AS OrderTargetAmount,
                og.GoodAmount AS OrderGoodAmount,
                sg.GoodAmount AS ShiftGoodAmount
            FROM OpenMes m
            INNER JOIN [{_schema}].[DimWorkcenter] w ON w.ID = m.WorkcenterID
            LEFT JOIN [{_schema}].[DimWorkcenterExt] wx ON wx.ID = w.ID
            LEFT JOIN [{_schema}].[DimMdaOperation] op ON op.ID = m.OperationID
            LEFT JOIN [{_schema}].[DimShiftEvent] sh ON sh.ID = m.ShiftID
            OUTER APPLY
            (
                SELECT TOP (1)
                    ds.Name AS StateName,
                    ds.CategoryName,
                    ds.Color,
                    ds.CategoryColor,
                    st.Starttime AS StateStarttime,
                    st.Endtime AS StateEndtime,
                    st.CustomText
                FROM [{_schema}].[FactMdaState] st
                INNER JOIN [{_schema}].[DimMdaState] ds ON ds.ID = st.StateID
                WHERE st.MesID = m.ID
                ORDER BY st.Starttime DESC, st.ID
            ) os
            LEFT JOIN OrderGood og ON og.OperationID = m.OperationID
            LEFT JOIN ShiftGood sg ON sg.WorkcenterID = m.WorkcenterID AND sg.ShiftID = m.ShiftID
            WHERE NULLIF(LTRIM(RTRIM(w.Code)), '') IS NOT NULL
              {workcenterPredicate}
              AND (@workcenterGroup IS NULL OR wx.CostCenter = @workcenterGroup)
            ORDER BY
                COALESCE(
                    NULLIF(LTRIM(RTRIM(wx.ErpCode)), ''),
                    NULLIF(LTRIM(RTRIM(w.ErpCode)), ''),
                    w.Code),
                w.Code;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddParameter(command, "@maxRows", Math.Clamp(filter.MaxRows, 1, 5000));

        for (var index = 0; index < requestedWorkcenters.Count; index++)
        {
            AddParameter(
                command,
                workcenterParameterNames[index],
                requestedWorkcenters[index]);
        }

        AddParameter(
            command,
            "@workcenterGroup",
            string.IsNullOrWhiteSpace(filter.WorkcenterGroup)
                ? null
                : filter.WorkcenterGroup.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<MesMachineOverviewRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MesMachineOverviewRecord
            {
                WorkcenterId = GetGuid(reader, "WorkcenterID") ?? Guid.Empty,
                WorkcenterCode = GetString(reader, "WorkcenterCode"),
                WorkcenterDescription = GetString(reader, "WorkcenterDescription"),
                WorkcenterDesignation = GetString(reader, "WorkcenterDesignation"),
                WorkcenterGroup = GetString(reader, "WorkcenterGroup"),
                ShiftId = GetGuid(reader, "ShiftID"),
                ShiftName = GetString(reader, "ShiftName"),
                ShiftStartTime = GetDateTime(reader, "ShiftStarttime"),
                ShiftEndTime = GetDateTime(reader, "ShiftEndtime"),
                OperationId = GetGuid(reader, "OperationID"),
                OrderCode = GetString(reader, "OrderCode"),
                ProductCode = GetString(reader, "ProductCode"),
                ProductDescription = GetString(reader, "ProductDescription"),
                StateName = GetString(reader, "StateName"),
                StateCategory = GetString(reader, "StateCategory"),
                StateColor = GetString(reader, "StateColor"),
                StateCategoryColor = GetString(reader, "StateCategoryColor"),
                StateStartedAt = GetDateTime(reader, "StateStarttime"),
                StateEndedAt = GetDateTime(reader, "StateEndtime"),
                StateUserText = GetString(reader, "StateUserText"),
                PlannedPerformancePerMinute = GetDecimal(reader, "PlannedPerformancePerMinute"),
                CurrentPerformancePerMinute = GetDecimal(reader, "CurrentPerformancePerMinute"),
                OrderTargetAmount = GetDecimal(reader, "OrderTargetAmount"),
                OrderGoodAmount = GetDecimal(reader, "OrderGoodAmount"),
                ShiftGoodAmount = GetDecimal(reader, "ShiftGoodAmount")
            });
        }

        return result;
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

        var connectionString =
            $"Server={server};Database={database};Integrated Security=True;Encrypt={encrypt};TrustServerCertificate={trust};Connect Timeout={timeout};Application Name=DMS MES Live Overview;";

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

            if (method?.ReturnType == typeof(string) && method.Invoke(_settings, null) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static DbConnection CreateSqlServerConnection(string connectionString)
    {
        var type = Type.GetType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient", throwOnError: false)
                   ?? Type.GetType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient", throwOnError: false);

        if (type is null)
        {
            throw new InvalidOperationException(
                "No SQL Server provider is available. The MES reporting integration must reference Microsoft.Data.SqlClient or System.Data.SqlClient.");
        }

        return (DbConnection)(Activator.CreateInstance(type, connectionString)
            ?? throw new InvalidOperationException("Could not create SQL Server connection."));
    }

    private DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
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

    private static void AddGuidParameter(DbCommand command, string name, Guid? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.Guid;
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private string? ReadString(params string[] names)
    {
        foreach (var name in names)
        {
            var property = _settings.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property?.GetValue(_settings) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private int ReadInt(int fallback, params string[] names)
    {
        foreach (var name in names)
        {
            var property = _settings.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var value = property?.GetValue(_settings);
            if (value is int intValue)
            {
                return intValue;
            }

            if (value is not null && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private bool ReadBool(bool fallback, params string[] names)
    {
        foreach (var name in names)
        {
            var property = _settings.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var value = property?.GetValue(_settings);
            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is not null && bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static int GetOrdinal(DbDataReader reader, string name) => reader.GetOrdinal(name);

    private static string GetString(DbDataReader reader, string name)
    {
        var ordinal = GetOrdinal(reader, name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private static Guid? GetGuid(DbDataReader reader, string name)
    {
        var ordinal = GetOrdinal(reader, name);
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value is Guid guid ? guid : Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out guid) ? guid : null;
    }

    private static DateTime? GetDateTime(DbDataReader reader, string name)
    {
        var ordinal = GetOrdinal(reader, name);
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value is DateTime dateTime ? dateTime : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    private static int? GetInt(DbDataReader reader, string name)
    {
        var ordinal = GetOrdinal(reader, name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static decimal? GetDecimal(DbDataReader reader, string name)
    {
        var ordinal = GetOrdinal(reader, name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }
}
