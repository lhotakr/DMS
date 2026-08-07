using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DMS.Desktop.Logging;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkAuditLoggingView : UserControl
{
    private static readonly HashSet<string> AuditLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUDIT",
        "AUDIT_CREATE",
        "AUDIT_DELETE",
        "CONFIG_CHANGED",
        "WORKFLOW_CHANGED",
        "SECURITY_CHANGED"
    };

    private static readonly HashSet<string> ErrorLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "ERROR",
        "TX_ERROR",
        "TX_FAIL"
    };

    private readonly DmsLogger _logger;
    private readonly DmsLogReader _reader;
    private readonly Func<string, string> _translate;
    private readonly Action<string> _executeTransaction;
    private readonly string _currentUser;
    private IReadOnlyList<DmsLogEntry> _entries = Array.Empty<DmsLogEntry>();

    public FrameworkAuditLoggingView(
        DmsLogger logger,
        DmsLogReader reader,
        string currentUser,
        Func<string, string> translate,
        Action<string> executeTransaction)
    {
        InitializeComponent();

        _logger = logger;
        _reader = reader;
        _currentUser = currentUser;
        _translate = translate;
        _executeTransaction = executeTransaction;

        ApplyLocalization();
        Loaded += (_, _) => Reload();
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
        TitleText.Text = T("Framework.FW05.Title", "FW05 — Audit and logging");
        SubtitleText.Text = T(
            "Framework.FW05.Description",
            "Central overview of log availability, standardized audit events and recent failures.");

        ReloadButton.Content = T("Framework.FW05.Reload", "Reload");
        OpenLogButton.Content = T("Framework.FW05.OpenLog", "Open LOG03");
        CopyButton.Content = T("Framework.FW05.Copy", "Copy summary");
        TestButton.Content = T("Framework.FW05.Test", "Write health event");

        TodayLabel.Text = T("Framework.FW05.Today", "Today's records");
        ErrorsLabel.Text = T("Framework.FW05.Errors", "Errors");
        AuditLabel.Text = T("Framework.FW05.Audit", "Audit records");
        WriteLabel.Text = T("Framework.FW05.WriteState", "Log write access");

        LevelColumn.Header = T("Framework.FW05.Column.Level", "Event level");
        CountColumn.Header = T("Framework.FW05.Column.Count", "Count");
        LastColumn.Header = T("Framework.FW05.Column.Last", "Last occurrence");
        DescriptionColumn.Header = T("Framework.FW05.Column.Description", "Framework meaning");

        TimeColumn.Header = T("Framework.FW05.Column.Time", "Time");
        RecentLevelColumn.Header = T("Framework.FW05.Column.Level", "Event level");
        RecentSummaryColumn.Header = T("Framework.FW05.Column.Summary", "Recent errors and framework events");
        RecentUserColumn.Header = T("Framework.FW05.Column.User", "User");
        CorrelationColumn.Header = T("Framework.FW05.Column.Correlation", "Correlation ID");

        FooterText.Text = T(
            "Framework.FW05.Footer",
            "Log content remains technical English. UI labels are localized. LOG03 provides detailed filtering and raw-line inspection.");
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => Reload();

    private void OpenLogButton_Click(object sender, RoutedEventArgs e) =>
        _executeTransaction("LOG03");

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var builder = new StringBuilder();
        builder.AppendLine(TitleText.Text);
        builder.AppendLine($"LogsRoot={_logger.LogsRootPath}");
        builder.AppendLine($"Records={_entries.Count}");
        builder.AppendLine($"Errors={_entries.Count(x => ErrorLevels.Contains(x.Level))}");
        builder.AppendLine($"Audit={_entries.Count(x => AuditLevels.Contains(x.Level))}");
        builder.AppendLine();

        foreach (var group in _entries
                     .GroupBy(x => x.Level, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(x => x.Count()))
        {
            builder.AppendLine($"{group.Key}: {group.Count()}");
        }

        Clipboard.SetText(builder.ToString());
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var context = new DmsAuditContext
        {
            TransactionCode = "FW05",
            ModuleCode = "ADMIN",
            Area = "Logging",
            Entity = "DmsLogger",
            User = _currentUser
        };

        _logger.FrameworkEvent(
            DmsAuditEventNames.FrameworkHealthCheck,
            context,
            "WRITE_TEST",
            "Manual framework logging health event.");

        Reload();
    }

    private void Reload()
    {
        _entries = _reader.ReadDay(_logger.LogsRootPath, DateTime.Today);

        TodayValue.Text = _entries.Count.ToString("N0");
        ErrorsValue.Text = _entries.Count(x => ErrorLevels.Contains(x.Level)).ToString("N0");
        AuditValue.Text = _entries.Count(x => AuditLevels.Contains(x.Level)).ToString("N0");

        var writable = _logger.TryWriteProbe(out var writeDetail);
        WriteValue.Text = writable
            ? T("Framework.FW05.Writable", "Writable")
            : T("Framework.FW05.NotWritable", "Not writable");
        WriteValue.ToolTip = writeDetail;

        LevelsGrid.ItemsSource = _entries
            .GroupBy(x => x.Level, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LogLevelSummary(
                group.Key,
                group.Count(),
                group.Max(x => x.Timestamp).ToString("yyyy-MM-dd HH:mm:ss"),
                DescribeLevel(group.Key)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Level)
            .ToList();

        RecentGrid.ItemsSource = _entries
            .Where(x =>
                ErrorLevels.Contains(x.Level) ||
                x.Level.StartsWith("FRAMEWORK_", StringComparison.OrdinalIgnoreCase) ||
                x.Level is "CONFIG_CHANGED" or "WORKFLOW_CHANGED" or "SECURITY_CHANGED")
            .OrderByDescending(x => x.Timestamp)
            .Take(100)
            .ToList();
    }

    private string DescribeLevel(string level) => level.ToUpperInvariant() switch
    {
        "TX_START" => T("Framework.FW05.Level.TxStart", "Transaction execution started."),
        "TX_OK" => T("Framework.FW05.Level.TxOk", "Transaction completed successfully."),
        "TX_ERROR" or "TX_FAIL" => T("Framework.FW05.Level.TxFail", "Transaction execution failed."),
        "AUDIT_CREATE" => T("Framework.FW05.Level.AuditCreate", "A business entity was created."),
        "AUDIT" => T("Framework.FW05.Level.Audit", "A field-level business value changed."),
        "AUDIT_DELETE" => T("Framework.FW05.Level.AuditDelete", "A business entity was deleted or deactivated."),
        "CONFIG_CHANGED" => T("Framework.FW05.Level.Config", "Runtime or application configuration changed."),
        "WORKFLOW_CHANGED" => T("Framework.FW05.Level.Workflow", "Workflow state or approval changed."),
        "SECURITY_CHANGED" => T("Framework.FW05.Level.Security", "User, role or permission configuration changed."),
        "FRAMEWORK_DIAGNOSTIC" => T("Framework.FW05.Level.Diagnostic", "Framework diagnostic event."),
        "FRAMEWORK_HEALTH" => T("Framework.FW05.Level.Health", "Framework health check."),
        _ => T("Framework.FW05.Level.Other", "Existing application log event.")
    };

    private sealed record LogLevelSummary(
        string Level,
        int Count,
        string LastOccurrence,
        string Description);
}
