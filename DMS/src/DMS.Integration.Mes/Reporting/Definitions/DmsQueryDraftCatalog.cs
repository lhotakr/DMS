using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DMS.Integration.Mes.Reporting.Definitions;

/// <summary>
/// Administrative query inventory and versioned text drafts. A draft never replaces executable SQL.
/// In particular, these records are not consumed by MES06 or production services.
/// </summary>
public sealed class DmsQueryDescriptor
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Transaction { get; init; } = string.Empty;
    public string DataSource { get; init; } = "MES";
    public string SourceFile { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string? BuiltInSqlCode { get; init; }
    public bool HasBuiltInSql => !string.IsNullOrEmpty(BuiltInSqlCode);
}

public sealed class DmsQueryRevision
{
    public int Version { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTimeOffset SavedUtc { get; set; }
    public string SqlSha256 { get; set; } = string.Empty;
}

public sealed class DmsQueryDraft
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Transaction { get; set; } = string.Empty;
    public string DataSource { get; set; } = "MES";
    public string SourceFile { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<DmsQueryRevision> History { get; set; } = new();
}

public sealed class DmsQueryDraftCatalog
{
    private readonly string _file;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public DmsQueryDraftCatalog(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot)) throw new ArgumentException("Configuration root is required.", nameof(configRoot));
        _file = Path.Combine(configRoot, "DataModel", "dms-query-drafts.json");
    }

    public string FilePath => _file;

    // Only verified source-file/method locations. This is not a list of executable SQL templates.
    // More SQL bodies can be imported after their parameter/output contracts are reviewed.
    public static IReadOnlyList<DmsQueryDescriptor> Inventory { get; } = new List<DmsQueryDescriptor>
    {
        Q("MES06_PRODUCTION", "Výroba", "MES06", "Reporting/MesReportingDataService.cs", "GetProductionAsync", "PRODUCTION"),
        Q("MES06_STATES", "Stavy", "MES06", "Reporting/MesReportingDataService.cs", "GetStatesAsync", "STATES"),
        Q("MES06_COUNTERS", "Čítače", "MES06", "Reporting/MesReportingDataService.cs", "GetCountersAsync", "COUNTERS"),
        Q("MES06_WORKCENTERS", "Pracoviště", "MES06", "Reporting/MesReportingDataService.cs", "GetWorkcentersAsync"),
        Q("ORD10_ORDERS", "Zakázky", "ORD10", "Orders/MesOrderOverviewDataService.cs", "GetOrdersAsync"),
        Q("ORD10_SEARCH", "Rozšířené hledání", "ORD10", "Orders/MesOrderOverviewDataService.cs", "SearchOrdersAsync"),
        Q("ORD10_OPERATIONS", "Operace zakázky", "ORD10", "Orders/MesOrderOverviewDataService.cs", "GetOperationsAsync"),
        Q("MES06_SCRAP", "Odpady", "MES06", "Reporting/MesScrapDetailDataService.cs", "GetReportAsync"),
        Q("MES06_COUNTER_CATALOG", "Katalog čítačů", "MES06", "Reporting/MesScrapDetailDataService.cs", "GetCounterCatalogAsync"),
        Q("MES06_PLACHTA", "Výrobní plachta", "MES06", "Reporting/MesPlachtaReportService.cs", "GetReportAsync"),
        Q("MES06_OEE", "OEE", "MES06", "Reporting/MesReportingEnrichmentService.cs", "GetOeeReportAsync"),
        Q("MES06_PROCESS", "Procesní hodnoty", "MES06", "Reporting/MesReportingEnrichmentService.cs", "GetProcessValuesAsync"),
        Q("MES06_GRAPH", "Výrobní graf", "MES06", "Reporting/MesReportingEnrichmentService.cs", "GetProductionGraphRowsAsync"),
        Q("MES06_SHIFTS", "Události směn", "MES06", "Reporting/MesReportingEnrichmentService.cs", "GetShiftEventsAsync"),
        Q("MES06_COUNTER_REPORT", "Přehled čítačů", "MES06", "Reporting/MesReportingEnrichmentService.cs", "GetCounterReportAsync"),
        Q("MES06_BONUS", "Bonusové reporty", "MES06", "Reporting/MesReportingEnrichmentService.cs", "GetBonusReportAsync"),
        Q("MES_LIVE_OVERVIEW", "Živá výroba", "MES", "Live/MesLiveOverviewDataService.cs", "GetOverviewAsync"),
        Q("MES_WORKCENTER", "Přehled pracoviště", "MES05", "Workcenters/MesWorkcenterDashboardService.cs", "GetDashboardAsync"),
        Q("SCR10_QUEUE", "Fronta zakázek", "SCR10", "Screens/Scr10QueueDataService.cs", "GetOrdersAsync"),
        Q("SCR10_STATES", "Aktuální stavy", "SCR10", "Screens/Scr10QueueDataService.cs", "GetCurrentStatesAsync"),
        Q("MES_DB_HEALTH", "Kontrola připojení", "MES00", "Database/MesConnectionHealthService.cs", "CheckAsync")
    };

    private static DmsQueryDescriptor Q(string code, string title, string transaction, string path, string method, string? builtIn = null) => new()
    {
        Code = code, Title = title, Transaction = transaction,
        SourceFile = "DMS/src/DMS.Integration.Mes/" + path, Method = method,
        BuiltInSqlCode = builtIn
    };

    public (List<DmsQueryDraft> Drafts, string Fingerprint) Load()
    {
        if (!File.Exists(_file)) return (new List<DmsQueryDraft>(), "(missing)");
        var data = File.ReadAllBytes(_file);
        if (data.LongLength > 8_000_000) throw new InvalidOperationException("Query catalog exceeds 8 MB.");
        var drafts = JsonSerializer.Deserialize<List<DmsQueryDraft>>(data, JsonOptions) ?? new();
        ValidateAll(drafts);
        return (drafts, Fingerprint(data));
    }

    public string Save(IReadOnlyList<DmsQueryDraft> drafts, string expectedFingerprint)
    {
        ValidateAll(drafts);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        // Share-exclusive lock serializes writes from different administrators on the same share.
        using var locker = new FileStream(_file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var actual = File.Exists(_file) ? Fingerprint(File.ReadAllBytes(_file)) : "(missing)";
        if (!string.Equals(actual, expectedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Query drafts were changed by another administrator. Reload before saving.");
        var json = JsonSerializer.Serialize(drafts, JsonOptions);
        var bytes = new UTF8Encoding(false).GetBytes(json);
        if (bytes.Length > 8_000_000) throw new InvalidOperationException("Query catalog exceeds 8 MB.");
        DmsDataSourceCatalog.SaveAtomic(_file, json);
        return Fingerprint(File.ReadAllBytes(_file));
    }

    public static string HashSql(string text) => Fingerprint(Encoding.UTF8.GetBytes(text ?? ""));
    private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void ValidateAll(IReadOnlyList<DmsQueryDraft> items)
    {
        if (items is null || items.Count > 200) throw new InvalidOperationException("Too many query drafts.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in items)
        {
            if (d is null || !Regex.IsMatch(d.Code ?? "", "^[A-Z][A-Z0-9_]{2,63}$") || !seen.Add(d.Code))
                throw new InvalidOperationException("Duplicate or invalid query code.");
            if ((d.Title?.Length ?? 0) > 180 || (d.Transaction?.Length ?? 0) > 60 ||
                (d.SourceFile?.Length ?? 0) > 400 || (d.Method?.Length ?? 0) > 200 ||
                (d.Notes?.Length ?? 0) > 2000 || (d.Sql?.Length ?? 0) > 50_000 ||
                (d.UpdatedBy?.Length ?? 0) > 180 || d.Version < 1 || d.Version > 100_000)
                throw new InvalidOperationException("Invalid draft metadata or SQL length.");
            if (d.History is null || d.History.Count > 30) throw new InvalidOperationException("Invalid revision history.");
            if (d.History.Any(v => v is null || v.Version < 1 || (v.Sql?.Length ?? 0) > 50_000 ||
                (v.Notes?.Length ?? 0) > 2000 || (v.User?.Length ?? 0) > 180))
                throw new InvalidOperationException("Invalid query revision.");
        }
    }
}
