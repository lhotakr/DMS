using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DMS.Integration.Mes.Reporting.Definitions;

/// <summary>Administrative SQL templates for the three stable, ordinal-mapped MES06 reports.</summary>
public static class MesSqlOverrideCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true
    };
    public static readonly IReadOnlyDictionary<string, int> ColumnCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["PRODUCTION"] = 17, ["STATES"] = 13, ["COUNTERS"] = 9
    };

    public static string FilePath(string configRoot) =>
        Path.Combine(configRoot, "DataModel", "mes06-sql-overrides.json");

    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8), Options)
                ?? new(), StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(string path, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            if (!ColumnCounts.ContainsKey(pair.Key)) throw new InvalidOperationException("Unsupported report query: " + pair.Key);
            Validate(pair.Value);
        }
        DmsDataSourceCatalog.SaveAtomic(path, JsonSerializer.Serialize(values, Options));
    }

    // Falls back to compiled-in SQL when no administrative override exists.
    // Invalid overrides stop the report rather than secretly executing unvalidated SQL.
    public static string Resolve(string path, string code, string builtInQuery, string schema)
    {
        if (!Regex.IsMatch(schema, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
            throw new InvalidOperationException("Invalid MES reporting schema.");
        var values = Load(path);
        if (!values.TryGetValue(code, out var sql) || string.IsNullOrWhiteSpace(sql)) return builtInQuery;
        Validate(sql);
        return sql.Replace("{schema}", schema, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Defense in depth, NOT a SQL authorization boundary. Database credentials must
    /// remain read-only; edited SQL is only available to DMS_ADMIN.
    /// </summary>
    public static void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 30000)
            throw new InvalidOperationException("SQL template is empty or too large.");
        var q = sql.Trim();
        if (!Regex.IsMatch(q, @"\ASELECT\s+TOP\s*\(\s*@maxRows\s*\)", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Only a bounded SELECT TOP (@maxRows) query is accepted.");
        var semicolonPos = q.IndexOf(';');
        if (q.Count(c => c == ';') > 1 || (semicolonPos >= 0 && semicolonPos != q.Length - 1))
            throw new InvalidOperationException("Only one SQL statement is allowed.");
        if (q.Contains("--", StringComparison.Ordinal) || q.Contains("/*", StringComparison.Ordinal) ||
            q.Contains("*/", StringComparison.Ordinal) || q.Contains('"') || q.Replace("N''", "", StringComparison.OrdinalIgnoreCase).Contains('\''))
            throw new InvalidOperationException("Comments and SQL string literals are not supported in editable templates.");
        var banned = @"\b(EXEC(?:UTE)?|INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|GRANT|DENY|REVOKE|INTO|OPENROWSET|OPENQUERY|OPENDATASOURCE|BULK|WAITFOR|UNION|INTERSECT|EXCEPT|APPLY|OUTPUT|SET|USE|BACKUP|RESTORE|SHUTDOWN|DBCC|XP_[A-Z0-9_]+|SP_[A-Z0-9_]+)\b";
        if (Regex.IsMatch(q, banned, RegexOptions.IgnoreCase))
            throw new InvalidOperationException("SQL contains a blocked operation.");
        var refs = Regex.Matches(q, @"\b(?:FROM|JOIN)\s+(?:\[?(?:\{schema\}|[a-zA-Z_][a-zA-Z0-9_]*)\]?\.)?\[?([a-zA-Z_][a-zA-Z0-9_]*)\]?", RegexOptions.IgnoreCase);
        if (refs.Count == 0) throw new InvalidOperationException("No table reference found.");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "FactMdaMes", "DimWorkcenter", "DimMdaOperation", "FactMdaState", "DimMdaState", "FactMdaCounter", "DimMdaCounter" };
        foreach (Match m in refs)
            if (!allowed.Contains(m.Groups[1].Value))
                throw new InvalidOperationException("Only known MES06 analytical tables are supported: " + m.Groups[1].Value);
        foreach (var required in new[]{"@maxRows", "@from", "@workcenter", "@orderCode", "@productCode"})
            if (!q.Contains(required, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Required filter is missing: " + required);
        if (!q.Contains("@to", StringComparison.OrdinalIgnoreCase) &&
            !q.Contains("@effectiveTo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Required end-time filter is missing.");
    }

    // Templates mirror the current read-only SQL shape. Read the query in the code editor,
    // make a copy, and test its output schema before activating it in a report.
    public static string BuiltIn(string code) => code.ToUpperInvariant() switch
    {
        "PRODUCTION" => """
SELECT TOP (@maxRows)
    mes.Starttime, mes.Endtime, wc.Code, wc.Description, wc.PlantName,
    op.OrderCode, op.OperationCode, op.OperationQuantity, op.ProductCode,
    op.ProductDescription, op.OrderQuantity, mes.PerformanceTotal,
    mes.PerformanceGood, mes.PerformanceBad, mes.PerformanceRework,
    mes.DurationUtilization, mes.DurationDown
FROM [{schema}].[FactMdaMes] AS mes
LEFT JOIN [{schema}].[DimWorkcenter] AS wc ON wc.ID = mes.WorkcenterID
LEFT JOIN [{schema}].[DimMdaOperation] AS op ON op.ID = mes.OperationID
WHERE mes.Starttime < @to AND COALESCE(mes.Endtime, @to) >= @from
    AND (@workcenter = N'' OR wc.Code = @workcenter)
    AND (@orderCode = N'' OR op.OrderCode = @orderCode)
    AND (@productCode = N'' OR op.ProductCode = @productCode)
ORDER BY mes.Starttime DESC;
""",
        "STATES" => """
SELECT TOP (@maxRows)
    wc.Code, op.OrderCode, op.ProductCode, stateFact.Starttime, stateFact.Endtime,
    stateDim.Name, stateDim.Description, stateDim.CategoryName, stateDim.IsSetup,
    stateDim.IsBreak, stateDim.IsCauselessFailure, stateFact.CustomText,
    DATEDIFF_BIG(MILLISECOND,
        CASE WHEN stateFact.Starttime < @from THEN @from ELSE stateFact.Starttime END,
        CASE WHEN COALESCE(stateFact.Endtime, @effectiveTo) > @effectiveTo
            THEN @effectiveTo ELSE COALESCE(stateFact.Endtime, @effectiveTo) END
    ) / 1000.0 AS DurationSeconds
FROM [{schema}].[FactMdaState] AS stateFact
LEFT JOIN [{schema}].[FactMdaMes] AS mes ON mes.ID = stateFact.MesID
LEFT JOIN [{schema}].[DimMdaState] AS stateDim ON stateDim.ID = stateFact.StateID
LEFT JOIN [{schema}].[DimWorkcenter] AS wc ON wc.ID = mes.WorkcenterID
LEFT JOIN [{schema}].[DimMdaOperation] AS op ON op.ID = mes.OperationID
WHERE stateFact.Starttime < @effectiveTo
    AND COALESCE(stateFact.Endtime, @effectiveTo) >= @from
    AND (@workcenter = N'' OR wc.Code = @workcenter)
    AND (@orderCode = N'' OR op.OrderCode = @orderCode)
    AND (@productCode = N'' OR op.ProductCode = @productCode)
ORDER BY stateFact.Starttime DESC;
""",
        "COUNTERS" => """
SELECT TOP (@maxRows)
    wc.Code, op.OrderCode, op.OperationCode, op.ProductCode,
    counterDim.Name, counterDim.Description, counterFact.Timestamp,
    counterFact.Value, counterFact.CustomText
FROM [{schema}].[FactMdaCounter] AS counterFact
LEFT JOIN [{schema}].[FactMdaMes] AS mes ON mes.ID = counterFact.MesID
LEFT JOIN [{schema}].[DimMdaCounter] AS counterDim ON counterDim.ID = counterFact.CounterID
LEFT JOIN [{schema}].[DimWorkcenter] AS wc ON wc.ID = mes.WorkcenterID
LEFT JOIN [{schema}].[DimMdaOperation] AS op ON op.ID = mes.OperationID
WHERE counterFact.Timestamp >= @from AND counterFact.Timestamp < @to
    AND (@workcenter = N'' OR wc.Code = @workcenter)
    AND (@orderCode = N'' OR op.OrderCode = @orderCode)
    AND (@productCode = N'' OR op.ProductCode = @productCode)
ORDER BY counterFact.Timestamp DESC;
""",
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    };
}
