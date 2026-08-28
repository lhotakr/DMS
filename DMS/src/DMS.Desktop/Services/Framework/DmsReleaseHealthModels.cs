namespace DMS.Desktop.Services.Framework;

public sealed record DmsReleaseHealthContext
{
    public string Environment { get; init; } = string.Empty;
    public string ConfigurationMode { get; init; } = string.Empty;
    public string ConfigurationRoot { get; init; } = string.Empty;
    public string DataRoot { get; init; } = string.Empty;
    public string DocumentsRoot { get; init; } = string.Empty;
    public string LogsRoot { get; init; } = string.Empty;
    public string BrandingRoot { get; init; } = string.Empty;
    public string ArticlesDataPath { get; init; } = string.Empty;
    public string SapMode { get; init; } = string.Empty;
    public string MesMode { get; init; } = string.Empty;
    public string DatabaseMode { get; init; } = string.Empty;
    public IReadOnlyList<DMS.Core.Transactions.TransactionDefinition> RuntimeTransactions { get; init; } =
        Array.Empty<DMS.Core.Transactions.TransactionDefinition>();
    public IReadOnlyList<string> RegisteredHandlerKeys { get; init; } = Array.Empty<string>();
    public DMS.Desktop.Performance.DmsPerformanceService Performance { get; init; } =
        DMS.Desktop.Performance.DmsPerformanceService.Current;
}

public sealed record DmsReleaseCheckResult
{
    public string Severity { get; init; } = "OK";
    public string FrameworkCode { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CheckCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string FixTransaction { get; init; } = string.Empty;
    public bool IsBlocking => Severity is "CRITICAL" or "ERROR";
}

public sealed record DmsReleaseHealthReport
{
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public string Verdict { get; init; } = "NOT READY";
    public double ReleaseQualityIndex { get; init; }
    public bool BuildAllowed { get; init; }
    public string Environment { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string InformationalVersion { get; init; } = string.Empty;
    public int CriticalCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int OkCount { get; init; }
    public int InfoCount { get; init; }
    public IReadOnlyList<DmsReleaseCheckResult> Results { get; init; } = Array.Empty<DmsReleaseCheckResult>();
}
