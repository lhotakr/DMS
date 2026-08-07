using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace DMS.Desktop.Performance;

public sealed class DmsPerformanceService
{
    private const int MaxSnapshots = 300;
    private const int MaxTransactions = 500;

    private readonly object _sampleLock = new();
    private readonly ConcurrentQueue<DmsPerformanceSnapshot> _snapshots = new();
    private readonly ConcurrentQueue<DmsTransactionPerformanceEntry> _transactions = new();

    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSampleUtc;
    private bool _hasCpuBaseline;

    public static DmsPerformanceService Current { get; } = new();

    private DmsPerformanceService()
    {
    }

    public void RecordTransaction(
        string transactionCode,
        double durationMs,
        string result)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(transactionCode)
            ? "UNKNOWN"
            : transactionCode.Trim().ToUpperInvariant();

        _transactions.Enqueue(new DmsTransactionPerformanceEntry
        {
            Timestamp = DateTime.Now,
            TransactionCode = normalizedCode,
            DurationMs = durationMs,
            Result = string.IsNullOrWhiteSpace(result) ? "UNKNOWN" : result.Trim().ToUpperInvariant()
        });

        TrimQueue(_transactions, MaxTransactions);
    }

    public DmsPerformanceSnapshot Sample(double uiFps, double uiDelayMs)
    {
        lock (_sampleLock)
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            var nowUtc = DateTime.UtcNow;
            var cpuTime = process.TotalProcessorTime;
            var cpuPercent = 0d;

            if (_hasCpuBaseline)
            {
                var elapsedMs = (nowUtc - _lastCpuSampleUtc).TotalMilliseconds;
                var cpuMs = (cpuTime - _lastCpuTime).TotalMilliseconds;

                if (elapsedMs > 0)
                {
                    cpuPercent =
                        cpuMs /
                        (elapsedMs * Math.Max(1, Environment.ProcessorCount)) *
                        100d;

                    cpuPercent = Math.Clamp(cpuPercent, 0d, 100d);
                }
            }

            _lastCpuSampleUtc = nowUtc;
            _lastCpuTime = cpuTime;
            _hasCpuBaseline = true;

            var snapshot = new DmsPerformanceSnapshot
            {
                Timestamp = DateTime.Now,
                CpuPercent = cpuPercent,
                WorkingSetMb = BytesToMb(process.WorkingSet64),
                PrivateMemoryMb = BytesToMb(process.PrivateMemorySize64),
                ManagedMemoryMb = BytesToMb(GC.GetTotalMemory(forceFullCollection: false)),
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                UiFps = Math.Max(0d, uiFps),
                UiDelayMs = Math.Max(0d, uiDelayMs)
            };

            _snapshots.Enqueue(snapshot);
            TrimQueue(_snapshots, MaxSnapshots);

            return snapshot;
        }
    }

    public IReadOnlyList<DmsPerformanceSnapshot> GetSnapshots() =>
        _snapshots.ToArray();

    public IReadOnlyList<DmsTransactionPerformanceEntry> GetTransactions() =>
        _transactions.ToArray();

    public IReadOnlyList<DmsTransactionPerformanceSummary> GetTransactionSummary()
    {
        return _transactions
            .ToArray()
            .GroupBy(
                x => x.TransactionCode,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .Select(x => x.DurationMs)
                    .OrderBy(x => x)
                    .ToArray();

                return new DmsTransactionPerformanceSummary
                {
                    TransactionCode = group.Key,
                    Count = ordered.Length,
                    AverageMs = ordered.Length == 0 ? 0 : ordered.Average(),
                    MaximumMs = ordered.Length == 0 ? 0 : ordered[^1],
                    P95Ms = Percentile(ordered, 0.95),
                    Failures = group.Count(x =>
                        x.Result is not "OK" and not "SUCCESS")
                };
            })
            .OrderByDescending(x => x.P95Ms)
            .ThenByDescending(x => x.AverageMs)
            .ToList();
    }

    public void ClearHistory()
    {
        while (_snapshots.TryDequeue(out _))
        {
        }

        while (_transactions.TryDequeue(out _))
        {
        }

        lock (_sampleLock)
        {
            _hasCpuBaseline = false;
            _lastCpuTime = TimeSpan.Zero;
            _lastCpuSampleUtc = default;
        }
    }

    public string ExportJson()
    {
        var model = new
        {
            exportedAt = DateTime.Now,
            machine = Environment.MachineName,
            process = Environment.ProcessId,
            snapshots = GetSnapshots(),
            transactions = GetTransactions(),
            transactionSummary = GetTransactionSummary()
        };

        return JsonSerializer.Serialize(
            model,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });
    }

    public string ExportCsv()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(
            "Timestamp;Transaction;DurationMs;Result");

        foreach (var entry in GetTransactions()
                     .OrderBy(x => x.Timestamp))
        {
            builder.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(';');
            builder.Append(EscapeCsv(entry.TransactionCode));
            builder.Append(';');
            builder.Append(entry.DurationMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(';');
            builder.Append(EscapeCsv(entry.Result));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public IReadOnlyList<DmsJsonProbeResult> ProbeJsonFiles(
        IEnumerable<string> paths)
    {
        var results = new List<DmsJsonProbeResult>();

        foreach (var path in paths
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            results.Add(ProbeJsonFile(path));
        }

        return results;
    }

    private static DmsJsonProbeResult ProbeJsonFile(string path)
    {
        if (!File.Exists(path))
        {
            return new DmsJsonProbeResult
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Exists = false,
                Status = "MISSING"
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // StreamReader detects and removes UTF BOMs (UTF-8/UTF-16/UTF-32).
            // JsonDocument.Parse(byte[]) expects JSON data without the UTF-8 BOM,
            // therefore reading the text first avoids false "0xEF" errors.
            string json;

            using (var reader = new StreamReader(
                       path,
                       detectEncodingFromByteOrderMarks: true))
            {
                json = reader.ReadToEnd();
            }

            using var document = JsonDocument.Parse(json);

            stopwatch.Stop();

            var fileInfo = new FileInfo(path);

            return new DmsJsonProbeResult
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Exists = true,
                SizeKb = fileInfo.Length / 1024d,
                ReadAndParseMs = stopwatch.Elapsed.TotalMilliseconds,
                RootType = document.RootElement.ValueKind.ToString(),
                Status = "OK"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new DmsJsonProbeResult
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Exists = true,
                SizeKb = new FileInfo(path).Length / 1024d,
                ReadAndParseMs = stopwatch.Elapsed.TotalMilliseconds,
                Status = "ERROR",
                Error = ex.Message
            };
        }
    }

    private static void TrimQueue<T>(
        ConcurrentQueue<T> queue,
        int maximum)
    {
        while (queue.Count > maximum &&
               queue.TryDequeue(out _))
        {
        }
    }

    private static double BytesToMb(long bytes) =>
        bytes / 1024d / 1024d;

    private static double Percentile(
        IReadOnlyList<double> ordered,
        double percentile)
    {
        if (ordered.Count == 0)
        {
            return 0d;
        }

        var index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        index = Math.Clamp(index, 0, ordered.Count - 1);
        return ordered[index];
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(';') &&
            !value.Contains('"') &&
            !value.Contains('\n') &&
            !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record DmsPerformanceSnapshot
{
    public DateTime Timestamp { get; init; }
    public double CpuPercent { get; init; }
    public double WorkingSetMb { get; init; }
    public double PrivateMemoryMb { get; init; }
    public double ManagedMemoryMb { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public double UiFps { get; init; }
    public double UiDelayMs { get; init; }
}

public sealed record DmsTransactionPerformanceEntry
{
    public DateTime Timestamp { get; init; }
    public string TransactionCode { get; init; } = string.Empty;
    public double DurationMs { get; init; }
    public string Result { get; init; } = string.Empty;

    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");
    public string DurationText => $"{DurationMs:0.0} ms";
}

public sealed record DmsTransactionPerformanceSummary
{
    public string TransactionCode { get; init; } = string.Empty;
    public int Count { get; init; }
    public double AverageMs { get; init; }
    public double MaximumMs { get; init; }
    public double P95Ms { get; init; }
    public int Failures { get; init; }

    public string AverageText => $"{AverageMs:0.0} ms";
    public string MaximumText => $"{MaximumMs:0.0} ms";
    public string P95Text => $"{P95Ms:0.0} ms";
}

public sealed record DmsJsonProbeResult
{
    public string Path { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public double SizeKb { get; init; }
    public double ReadAndParseMs { get; init; }
    public string RootType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public string SizeText => Exists ? $"{SizeKb:0.0} KB" : "—";
    public string LoadText => Exists ? $"{ReadAndParseMs:0.0} ms" : "—";
}
