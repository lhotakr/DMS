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
    private readonly SemaphoreSlim _metadataGate = new(1, 1);

    private WorkcenterMetadataSnapshot? _configuredWorkcenterMetadata;
    private bool _configuredWorkcenterMetadataResolved;

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
    /// Returns the real FASTEC workcenter groups when the SQL user can read a FASTEC
    /// configuration table/view that exposes Workcenter + Designation + Groups.
    /// The analytical ana snapshot contains CostCenter, but FASTEC Groups is a separate
    /// many-to-many concept, so CostCenter is intentionally NOT used as a fake fallback.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetWorkcenterGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var workcenters = await GetWorkcentersAsync(
            workcenterGroup: null,
            cancellationToken);

        return workcenters
            .SelectMany(item => item.Groups)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns only active workcenters (open FactMdaMes interval + current ShiftID).
    /// Designation/Groups are overlaid from the FASTEC configuration source when it is
    /// discoverable through INFORMATION_SCHEMA. Otherwise Designation falls back to
    /// ana.ErpCode and Groups stay empty rather than being confused with CostCenter.
    /// </summary>
    public async Task<IReadOnlyList<MesLiveWorkcenterRecord>> GetWorkcentersAsync(
        string? workcenterGroup = null,
        CancellationToken cancellationToken = default)
    {
        var baseRows = await LoadActiveWorkcentersFromAnaAsync(cancellationToken);
        var metadata = await GetConfiguredWorkcenterMetadataAsync(cancellationToken);

        var result = new List<MesLiveWorkcenterRecord>();

        foreach (var row in baseRows)
        {
            metadata.ByCode.TryGetValue(row.Code, out var configured);

            var designation = !string.IsNullOrWhiteSpace(configured?.Designation)
                ? configured!.Designation
                : row.Designation;

            var groups = configured?.Groups
                         ?? Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(workcenterGroup)
                && !groups.Any(group => string.Equals(
                    group,
                    workcenterGroup.Trim(),
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            result.Add(new MesLiveWorkcenterRecord
            {
                Id = row.Id,
                Code = row.Code,
                Description = row.Description,
                Designation = designation,
                Groups = groups,
                GroupName = string.Join(", ", groups)
            });
        }

        return result
            .OrderBy(item => item.DisplayDesignation, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<MesLiveWorkcenterRecord>> LoadActiveWorkcentersFromAnaAsync(
        CancellationToken cancellationToken)
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
                    w.Code) AS Designation
            FROM ActiveWorkcenters a
            INNER JOIN [{_schema}].[DimWorkcenter] w ON w.ID = a.WorkcenterID
            LEFT JOIN [{_schema}].[DimWorkcenterExt] wx ON wx.ID = w.ID
            WHERE NULLIF(LTRIM(RTRIM(w.Code)), '') IS NOT NULL;
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<MesLiveWorkcenterRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MesLiveWorkcenterRecord
            {
                Id = GetGuid(reader, "ID") ?? Guid.Empty,
                Code = GetString(reader, "Code").Trim(),
                Description = GetString(reader, "Description").Trim(),
                Designation = GetString(reader, "Designation").Trim()
            });
        }

        return result;
    }

    private async Task<WorkcenterMetadataSnapshot> GetConfiguredWorkcenterMetadataAsync(
        CancellationToken cancellationToken)
    {
        if (_configuredWorkcenterMetadataResolved)
        {
            return _configuredWorkcenterMetadata ?? new WorkcenterMetadataSnapshot();
        }

        await _metadataGate.WaitAsync(cancellationToken);
        try
        {
            if (!_configuredWorkcenterMetadataResolved)
            {
                _configuredWorkcenterMetadata =
                    await DiscoverConfiguredWorkcenterMetadataAsync(cancellationToken)
                    ?? new WorkcenterMetadataSnapshot();
                _configuredWorkcenterMetadataResolved = true;
            }

            return _configuredWorkcenterMetadata ?? new WorkcenterMetadataSnapshot();
        }
        finally
        {
            _metadataGate.Release();
        }
    }

    private async Task<WorkcenterMetadataSnapshot?> DiscoverConfiguredWorkcenterMetadataAsync(
        CancellationToken cancellationToken)
    {
        const string metadataSql = """
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE LOWER(COLUMN_NAME) IN
            (
                'designation',
                'groups',
                'group',
                'groupname',
                'groupnames',
                'workcentergroups',
                'code',
                'workcenter',
                'work center',
                'workcentercode'
            )
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;
            """;

        var columns = new List<MetadataColumnRow>();

        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = CreateCommand(connection, metadataSql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new MetadataColumnRow
                {
                    Schema = GetString(reader, "TABLE_SCHEMA"),
                    Table = GetString(reader, "TABLE_NAME"),
                    Column = GetString(reader, "COLUMN_NAME")
                });
            }
        }

        var candidates = columns
            .GroupBy(row => (row.Schema, row.Table))
            .Select(group =>
            {
                var names = group
                    .Select(row => row.Column)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new MetadataTableCandidate
                {
                    Schema = group.Key.Schema,
                    Table = group.Key.Table,
                    CodeColumn = FindColumn(
                        names,
                        "WorkcenterCode",
                        "WorkCenterCode",
                        "Workcenter",
                        "WorkCenter",
                        "Work center",
                        "Code"),
                    DesignationColumn = FindColumn(names, "Designation"),
                    GroupsColumn = FindColumn(
                        names,
                        "Groups",
                        "WorkcenterGroups",
                        "GroupNames",
                        "GroupName",
                        "Group")
                };
            })
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.CodeColumn)
                && !string.IsNullOrWhiteSpace(candidate.DesignationColumn)
                && !string.IsNullOrWhiteSpace(candidate.GroupsColumn))
            .OrderByDescending(candidate =>
                candidate.Table.Contains("workcenter", StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Table, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var candidate in candidates)
        {
            try
            {
                var codeColumn = QuoteIdentifier(candidate.CodeColumn!);
                var designationColumn = QuoteIdentifier(candidate.DesignationColumn!);
                var groupsColumn = QuoteIdentifier(candidate.GroupsColumn!);
                var source = $"{QuoteIdentifier(candidate.Schema)}.{QuoteIdentifier(candidate.Table)}";

                var sql = $"""
                    SELECT TOP (5000)
                        CONVERT(nvarchar(255), {codeColumn}) AS WorkcenterCode,
                        CONVERT(nvarchar(255), {designationColumn}) AS WorkcenterDesignation,
                        CONVERT(nvarchar(max), {groupsColumn}) AS WorkcenterGroups
                    FROM {source}
                    WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(255), {codeColumn}))), '') IS NOT NULL;
                    """;

                var accumulators = new Dictionary<string, WorkcenterMetadataAccumulator>(
                    StringComparer.OrdinalIgnoreCase);

                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);
                await using var command = CreateCommand(connection, sql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var code = GetString(reader, "WorkcenterCode").Trim();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    if (!accumulators.TryGetValue(code, out var accumulator))
                    {
                        accumulator = new WorkcenterMetadataAccumulator();
                        accumulators.Add(code, accumulator);
                    }

                    var designation = GetString(reader, "WorkcenterDesignation").Trim();
                    if (!string.IsNullOrWhiteSpace(designation))
                    {
                        accumulator.Designation = designation;
                    }

                    foreach (var groupName in ParseGroups(
                                 GetString(reader, "WorkcenterGroups")))
                    {
                        accumulator.Groups.Add(groupName);
                    }
                }

                if (accumulators.Count == 0)
                {
                    continue;
                }

                var snapshot = new WorkcenterMetadataSnapshot
                {
                    Source = $"{candidate.Schema}.{candidate.Table}"
                };

                foreach (var pair in accumulators)
                {
                    snapshot.ByCode[pair.Key] = new WorkcenterMetadataEntry
                    {
                        Designation = pair.Value.Designation,
                        Groups = pair.Value.Groups
                            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                            .ToList()
                    };
                }

                if (snapshot.ByCode.Values.Any(value =>
                    !string.IsNullOrWhiteSpace(value.Designation)
                    || value.Groups.Count > 0))
                {
                    return snapshot;
                }
            }
            catch
            {
                // Candidate may be a protected FASTEC configuration object. Try the next
                // metadata match; the live overview must keep working from ana read-only data.
            }
        }

        return null;
    }

    private static string? FindColumn(
        IEnumerable<string> availableColumns,
        params string[] candidateNames)
    {
        foreach (var candidate in candidateNames)
        {
            var match = availableColumns.FirstOrDefault(column =>
                string.Equals(column, candidate, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseGroups(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]")}]";

    public async Task<IReadOnlyList<MesMachineOverviewRecord>> GetOverviewAsync(
        MesMachineOverviewFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var configuredMetadata =
            await GetConfiguredWorkcenterMetadataAsync(cancellationToken);

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
                CAST('' AS nvarchar(1)) AS WorkcenterGroup,
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

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<MesMachineOverviewRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var workcenterCode = GetString(reader, "WorkcenterCode").Trim();
            configuredMetadata.ByCode.TryGetValue(workcenterCode, out var configured);

            var fallbackDesignation = GetString(reader, "WorkcenterDesignation").Trim();
            var designation = !string.IsNullOrWhiteSpace(configured?.Designation)
                ? configured!.Designation
                : fallbackDesignation;

            var groups = configured?.Groups ?? Array.Empty<string>();

            result.Add(new MesMachineOverviewRecord
            {
                WorkcenterId = GetGuid(reader, "WorkcenterID") ?? Guid.Empty,
                WorkcenterCode = workcenterCode,
                WorkcenterDescription = GetString(reader, "WorkcenterDescription"),
                WorkcenterDesignation = designation,
                WorkcenterGroup = string.Join(", ", groups),
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

        return result
            .OrderBy(item => item.WorkcenterDesignation, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.WorkcenterCode, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private sealed class MetadataColumnRow
    {
        public string Schema { get; init; } = string.Empty;
        public string Table { get; init; } = string.Empty;
        public string Column { get; init; } = string.Empty;
    }

    private sealed class MetadataTableCandidate
    {
        public string Schema { get; init; } = string.Empty;
        public string Table { get; init; } = string.Empty;
        public string? CodeColumn { get; init; }
        public string? DesignationColumn { get; init; }
        public string? GroupsColumn { get; init; }
    }

    private sealed class WorkcenterMetadataAccumulator
    {
        public string Designation { get; set; } = string.Empty;
        public HashSet<string> Groups { get; } = new(StringComparer.CurrentCultureIgnoreCase);
    }

    private sealed class WorkcenterMetadataEntry
    {
        public string Designation { get; init; } = string.Empty;
        public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();
    }

    private sealed class WorkcenterMetadataSnapshot
    {
        public string Source { get; init; } = string.Empty;
        public Dictionary<string, WorkcenterMetadataEntry> ByCode { get; } =
            new(StringComparer.OrdinalIgnoreCase);
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
