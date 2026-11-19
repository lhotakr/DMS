using System.Text;
using System.Text.Json;

namespace DMS.Integration.Mes.Reporting.Definitions;

/// <summary>Configuration metadata only. Passwords and connection strings are never stored here.</summary>
public sealed class DmsDataSourceEntry
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "XLSX";
    public string Location { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class DmsDataSourceCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true
    };
    private readonly string _path;

    public DmsDataSourceCatalog(string configRoot) =>
        _path = Path.Combine(configRoot, "DataModel", "dms-data-sources.json");

    public IReadOnlyList<DmsDataSourceEntry> Load()
    {
        var defaults = new List<DmsDataSourceEntry>
        {
            new() { Code = "MES", Name = "FASTEC MES (MESSET)", Kind = "MES_SQL", Location = "MESSET" },
            new() { Code = "SAP_CACHE", Name = "SAP local cache", Kind = "SAP_CACHE", Location = "Data/SAP" },
            new() { Code = "QUALITY", Name = "DMS Quality", Kind = "QA_JSON", Location = "Data/Quality" }
        };
        if (!File.Exists(_path)) return defaults;
        var data = JsonSerializer.Deserialize<List<DmsDataSourceEntry>>(File.ReadAllText(_path, Encoding.UTF8), Options)
            ?? new List<DmsDataSourceEntry>();
        foreach (var builtin in defaults)
            if (!data.Any(x => x.Code.Equals(builtin.Code, StringComparison.OrdinalIgnoreCase))) data.Insert(0, builtin);
        return data;
    }

    public void Save(IEnumerable<DmsDataSourceEntry> entries)
    {
        var items = entries.ToList();
        if (items.Count > 100) throw new InvalidOperationException("Maximum 100 data sources.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in items)
        {
            source.Code = source.Code.Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(source.Code, "^[A-Z][A-Z0-9_]{1,39}$"))
                throw new InvalidOperationException("Source code must contain 2-40 letters, digits or underscores.");
            if (!unique.Add(source.Code)) throw new InvalidOperationException("Duplicate source code: " + source.Code);
            if (source.Name.Length > 100 || source.Location.Length > 1000)
                throw new InvalidOperationException("Data source name or path is too long.");
            if (source.Kind is not ("MES_SQL" or "SAP_CACHE" or "QA_JSON" or "XLSX" or "CSV" or "JSON"))
                throw new InvalidOperationException("Unsupported data source kind: " + source.Kind);
            if (source.Code is "MES" or "SAP_CACHE" or "QUALITY")
            {
                var requiredKind = source.Code == "MES" ? "MES_SQL" : source.Code == "SAP_CACHE" ? "SAP_CACHE" : "QA_JSON";
                if (source.Kind != requiredKind) throw new InvalidOperationException("Built-in source kind is locked.");
            }
            if (source.Kind is "XLSX" or "CSV" or "JSON")
            {
                if (!Path.IsPathFullyQualified(source.Location))
                    throw new InvalidOperationException("File source requires an absolute path.");
                if (Path.GetExtension(source.Location).ToUpperInvariant() != "." + source.Kind)
                    throw new InvalidOperationException("File extension must match the selected kind.");
            }
            else if (source.Code is "MES" or "SAP_CACHE" or "QUALITY")
            {
                var required = source.Code == "MES" ? "MES_SQL" : source.Code == "SAP_CACHE" ? "SAP_CACHE" : "QA_JSON";
                if (source.Kind != required) throw new InvalidOperationException("Built-in source kind is locked.");
                // Built-in adapters use DMS configuration, not arbitrary connection strings.
                source.Location = source.Code == "MES" ? "MESSET" : source.Code == "SAP_CACHE" ? "Data/SAP" : "Data/Quality";
            }
        }
        SaveAtomic(_path, JsonSerializer.Serialize(items, Options));
    }

    internal static void SaveAtomic(string path, string json)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        try
        {
            if (File.Exists(path))
            {
                var backup = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".bak";
                File.Copy(path, backup);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
