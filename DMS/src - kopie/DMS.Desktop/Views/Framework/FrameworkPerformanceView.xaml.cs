using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DMS.Desktop.Performance;
using Microsoft.Win32;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkPerformanceView : UserControl
{
    private readonly DmsPerformanceService _performance;
    private readonly string _configRoot;
    private readonly string _dataRoot;
    private readonly string _articlesDataPath;
    private readonly string _currentUser;
    private readonly Func<string, string> _translate;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string> _log;

    private readonly DispatcherTimer _timer;
    private DateTime _lastTickUtc = DateTime.UtcNow;
    private long _renderFrames;
    private long _renderFramesAtLastTick;
    private double _lastFps;
    private double _lastUiDelayMs;

    public FrameworkPerformanceView(
        DmsPerformanceService performance,
        string configRoot,
        string dataRoot,
        string articlesDataPath,
        string currentUser,
        Func<string, string> translate,
        Action<string> executeTransaction,
        Action<string, string> log)
    {
        InitializeComponent();

        _performance = performance;
        _configRoot = configRoot;
        _dataRoot = dataRoot;
        _articlesDataPath = articlesDataPath;
        _currentUser = currentUser;
        _translate = translate;
        _executeTransaction = executeTransaction;
        _log = log;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _timer.Tick += Timer_Tick;

        ApplyLocalization();

        Loaded += FrameworkPerformanceView_Loaded;
        Unloaded += FrameworkPerformanceView_Unloaded;
        SizeChanged += (_, _) => DrawCharts();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TitleText.Text = T("Framework.FW08.Title", "FW08 — Performance monitor");
        SubtitleText.Text = T(
            "Framework.FW08.Description",
            "Live runtime performance, transaction timing, memory, UI responsiveness and JSON read/parse probes.");

        RefreshButton.Content = T("Framework.FW08.Refresh", "Refresh");
        ClearButton.Content = T("Framework.FW08.Clear", "Clear history");
        ExportCsvButton.Content = T("Framework.FW08.ExportCsv", "Export CSV");
        ExportJsonButton.Content = T("Framework.FW08.ExportJson", "Export JSON");
        GcButton.Content = T("Framework.FW08.Gc", "Run GC");
        LogButton.Content = T("Framework.FW08.Log", "Open LOG03");

        CpuLabel.Text = T("Framework.FW08.Cpu", "CPU");
        MemoryLabel.Text = T("Framework.FW08.WorkingSet", "Working set");
        ManagedLabel.Text = T("Framework.FW08.Managed", "Managed heap");
        FpsLabel.Text = T("Framework.FW08.Fps", "UI FPS");
        DelayLabel.Text = T("Framework.FW08.Delay", "UI delay");
        GcLabel.Text = T("Framework.FW08.GcCollections", "GC collections");

        CpuChartLabel.Text = T("Framework.FW08.Chart.Cpu", "CPU — last 60 samples");
        MemoryChartLabel.Text = T("Framework.FW08.Chart.Memory", "Working set — last 60 samples");
        FpsChartLabel.Text = T("Framework.FW08.Chart.Fps", "UI FPS — last 60 samples");

        TransactionColumn.Header = T("Framework.FW08.Column.Transaction", "Transaction");
        CountColumn.Header = T("Framework.FW08.Column.Count", "Count");
        AverageColumn.Header = T("Framework.FW08.Column.Average", "Average");
        P95Column.Header = T("Framework.FW08.Column.P95", "P95");
        MaximumColumn.Header = T("Framework.FW08.Column.Maximum", "Maximum");
        FailureColumn.Header = T("Framework.FW08.Column.Failures", "Failures");

        TimeColumn.Header = T("Framework.FW08.Column.Time", "Time");
        RecentTransactionColumn.Header = T("Framework.FW08.Column.Transaction", "Transaction");
        DurationColumn.Header = T("Framework.FW08.Column.Duration", "Duration");
        ResultColumn.Header = T("Framework.FW08.Column.Result", "Result");

        JsonFileColumn.Header = T("Framework.FW08.Column.File", "JSON file");
        JsonSizeColumn.Header = T("Framework.FW08.Column.Size", "Size");
        JsonLoadColumn.Header = T("Framework.FW08.Column.ReadParse", "Read + parse");
        JsonRootColumn.Header = T("Framework.FW08.Column.Root", "Root");
        JsonStatusColumn.Header = T("Framework.FW08.Column.Status", "Status");
        JsonPathColumn.Header = T("Framework.FW08.Column.Path", "Path");
        JsonErrorColumn.Header = T("Framework.FW08.Column.Error", "Error");

        FooterText.Text = T(
            "Framework.FW08.Footer",
            "Transaction timings measure the complete ExecuteTransaction path including rendering. JSON timings are explicit read-and-parse probes, not cache-hit statistics.");
    }

    private void FrameworkPerformanceView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        CompositionTarget.Rendering += CompositionTarget_Rendering;

        _lastTickUtc = DateTime.UtcNow;
        _renderFrames = 0;
        _renderFramesAtLastTick = 0;

        _timer.Start();

        RefreshAll();

        _log(
            "PERFORMANCE_MONITOR_OPEN",
            $"User={_currentUser}");
    }

    private void FrameworkPerformanceView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _timer.Stop();
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
    }

    private void CompositionTarget_Rendering(
        object? sender,
        EventArgs e)
    {
        _renderFrames++;
    }

    private void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTickUtc;

        if (elapsed.TotalSeconds <= 0)
        {
            return;
        }

        var renderedSinceLast =
            _renderFrames -
            _renderFramesAtLastTick;

        _lastFps =
            renderedSinceLast /
            elapsed.TotalSeconds;

        _lastUiDelayMs =
            Math.Max(
                0d,
                (elapsed - _timer.Interval).TotalMilliseconds);

        _lastTickUtc = now;
        _renderFramesAtLastTick = _renderFrames;

        RefreshRuntimeMetrics();
    }

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e) =>
        RefreshAll();

    private void ClearButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _performance.ClearHistory();
        _lastFps = 0d;
        _lastUiDelayMs = 0d;
        RefreshAll();

        _log(
            "PERFORMANCE_HISTORY_CLEAR",
            $"User={_currentUser}");
    }

    private void GcButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var before =
            GC.GetTotalMemory(
                forceFullCollection: false);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var after =
            GC.GetTotalMemory(
                forceFullCollection: false);

        FooterText.Text = string.Format(
            T(
                "Framework.FW08.GcResult",
                "GC completed. Managed heap: {0:0.0} MB → {1:0.0} MB."),
            before / 1024d / 1024d,
            after / 1024d / 1024d);

        _log(
            "PERFORMANCE_GC",
            $"BeforeBytes={before}; AfterBytes={after}; User={_currentUser}");

        RefreshRuntimeMetrics();
    }

    private void LogButton_Click(
        object sender,
        RoutedEventArgs e) =>
        _executeTransaction("LOG03");

    private void ExportCsvButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = T("Framework.FW08.ExportCsv", "Export CSV"),
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"dms-performance-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(
            dialog.FileName,
            _performance.ExportCsv());

        FooterText.Text = string.Format(
            T(
                "Framework.FW08.Exported",
                "Exported: {0}"),
            dialog.FileName);
    }

    private void ExportJsonButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = T("Framework.FW08.ExportJson", "Export JSON"),
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"dms-performance-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(
            dialog.FileName,
            _performance.ExportJson());

        FooterText.Text = string.Format(
            T(
                "Framework.FW08.Exported",
                "Exported: {0}"),
            dialog.FileName);
    }

    private void RefreshAll()
    {
        RefreshRuntimeMetrics();
        RefreshJsonProbe();
    }

    private void RefreshRuntimeMetrics()
    {
        var snapshot =
            _performance.Sample(
                _lastFps,
                _lastUiDelayMs);

        CpuValue.Text =
            $"{snapshot.CpuPercent:0.0} %";

        MemoryValue.Text =
            $"{snapshot.WorkingSetMb:0.0} MB";

        ManagedValue.Text =
            $"{snapshot.ManagedMemoryMb:0.0} MB";

        FpsValue.Text =
            $"{snapshot.UiFps:0.0}";

        DelayValue.Text =
            $"{snapshot.UiDelayMs:0.0} ms";

        GcValue.Text =
            $"{snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections}";

        TransactionSummaryGrid.ItemsSource =
            _performance.GetTransactionSummary();

        RecentGrid.ItemsSource =
            _performance
                .GetTransactions()
                .OrderByDescending(x => x.Timestamp)
                .Take(100)
                .ToList();

        DrawCharts();
    }

    private void RefreshJsonProbe()
    {
        var paths = new[]
        {
            Path.Combine(_configRoot, "transactions.json"),
            Path.Combine(_configRoot, "dms-modules.json"),
            Path.Combine(_configRoot, "dms-roles.json"),
            Path.Combine(_configRoot, "users.json"),
            Path.Combine(_configRoot, "dms-system-settings.json"),
            Path.Combine(_configRoot, "mes-integration.json"),
            Path.Combine(_configRoot, "mes-plc-bindings.json"),
            ResolveArticlesPath()
        };

        JsonGrid.ItemsSource =
            _performance.ProbeJsonFiles(paths);
    }

    private string ResolveArticlesPath()
    {
        if (!string.IsNullOrWhiteSpace(_articlesDataPath))
        {
            return _articlesDataPath;
        }

        return Path.Combine(
            _dataRoot,
            "Data",
            "articles.json");
    }

    private void DrawCharts()
    {
        var samples = _performance
            .GetSnapshots()
            .TakeLast(60)
            .ToList();

        DrawLine(
            CpuLine,
            CpuCanvas,
            samples.Select(x => x.CpuPercent).ToList(),
            fixedMaximum: 100d);

        DrawLine(
            MemoryLine,
            MemoryCanvas,
            samples.Select(x => x.WorkingSetMb).ToList(),
            fixedMaximum: null);

        DrawLine(
            FpsLine,
            FpsCanvas,
            samples.Select(x => x.UiFps).ToList(),
            fixedMaximum: null);
    }

    private static void DrawLine(
        System.Windows.Shapes.Polyline line,
        Canvas canvas,
        IReadOnlyList<double> values,
        double? fixedMaximum)
    {
        line.Points.Clear();

        if (values.Count == 0)
        {
            return;
        }

        var width =
            canvas.ActualWidth > 10
                ? canvas.ActualWidth
                : 300d;

        var height =
            canvas.ActualHeight > 10
                ? canvas.ActualHeight
                : 100d;

        var maximum =
            fixedMaximum ??
            Math.Max(
                1d,
                values.Max());

        var minimum =
            fixedMaximum.HasValue
                ? 0d
                : Math.Min(
                    values.Min(),
                    maximum);

        var range =
            Math.Max(
                0.001d,
                maximum - minimum);

        for (var index = 0;
             index < values.Count;
             index++)
        {
            var x =
                values.Count == 1
                    ? 0d
                    : index *
                      width /
                      (values.Count - 1d);

            var normalized =
                (values[index] - minimum) /
                range;

            var y =
                height -
                normalized * height;

            line.Points.Add(
                new Point(
                    x,
                    Math.Clamp(y, 0d, height)));
        }
    }
}
