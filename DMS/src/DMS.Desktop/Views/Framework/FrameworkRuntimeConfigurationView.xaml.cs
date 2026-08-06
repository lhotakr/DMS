using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkRuntimeConfigurationView : UserControl
{
    private readonly string _environment;
    private readonly string _configurationMode;
    private readonly string _configRoot;
    private readonly string _dataRoot;
    private readonly string _documentsRoot;
    private readonly string _logsRoot;
    private readonly string _brandingRoot;
    private readonly string _articlesDataPath;
    private readonly string _sapMode;
    private readonly string _mesMode;
    private readonly string _databaseMode;
    private readonly Func<string, string> _translate;
    private readonly Action<string, string> _log;

    public FrameworkRuntimeConfigurationView(
        string environment,
        string configurationMode,
        string configRoot,
        string dataRoot,
        string documentsRoot,
        string logsRoot,
        string brandingRoot,
        string articlesDataPath,
        string sapMode,
        string mesMode,
        string databaseMode,
        Func<string, string> translate,
        Action<string, string> log)
    {
        InitializeComponent();

        _environment = environment;
        _configurationMode = configurationMode;
        _configRoot = configRoot;
        _dataRoot = dataRoot;
        _documentsRoot = documentsRoot;
        _logsRoot = logsRoot;
        _brandingRoot = brandingRoot;
        _articlesDataPath = articlesDataPath;
        _sapMode = sapMode;
        _mesMode = mesMode;
        _databaseMode = databaseMode;
        _translate = translate;
        _log = log;

        TitleText.Text = T("Framework.FW03.Title", "FW03 — Runtime configuration");
        SubtitleText.Text = T(
            "Framework.FW03.Description",
            "Shows the effective runtime paths, modes and active configuration files used by this client.");
        ReloadButton.Content = T("Framework.Runtime.Reload", "Reload overview");
        FooterText.Text = T(
            "Framework.Runtime.Footer",
            "This screen is read-only. Configuration changes are made in the dedicated administration transactions.");

        CategoryColumn.Header = T("Framework.Runtime.Column.Category", "Category");
        ItemColumn.Header = T("Framework.Runtime.Column.Item", "Item");
        ValueColumn.Header = T("Framework.Runtime.Column.Value", "Effective value");
        StateColumn.Header = T("Framework.Runtime.Column.State", "State");
        DetailsColumn.Header = T("Framework.Runtime.Column.Details", "Details");

        Loaded += (_, _) => Reload();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => Reload();

    private void Reload()
    {
        var rows = new List<RuntimeConfigurationRow>();

        AddValue(rows, "Runtime", "Environment", _environment);
        AddValue(rows, "Runtime", "Configuration mode", _configurationMode);
        AddValue(rows, "Runtime", "SAP mode", _sapMode);
        AddValue(rows, "Runtime", "MES mode", _mesMode);
        AddValue(rows, "Runtime", "Database mode", _databaseMode);

        AddDirectory(rows, "Paths", "Configuration root", _configRoot);
        AddDirectory(rows, "Paths", "Data root", _dataRoot);
        AddDirectory(rows, "Paths", "Documents root", _documentsRoot);
        AddDirectory(rows, "Paths", "Logs root", _logsRoot);
        AddDirectory(rows, "Paths", "Branding root", _brandingRoot);
        AddFile(rows, "Paths", "Articles data", _articlesDataPath, optional: true);

        AddJson(rows, "Configuration", "Transactions", Path.Combine(_configRoot, "transactions.json"), optional: false);
        AddJson(rows, "Configuration", "Modules", Path.Combine(_configRoot, "dms-modules.json"), optional: false);
        AddJson(rows, "Configuration", "Roles", Path.Combine(_configRoot, "dms-roles.json"), optional: false);
        AddJson(rows, "Configuration", "Users", Path.Combine(_configRoot, "users.json"), optional: false);
        AddJson(rows, "Configuration", "System settings", Path.Combine(_configRoot, "dms-system-settings.json"), optional: true);
        AddJson(rows, "Configuration", "MES integration", Path.Combine(_configRoot, "mes-integration.json"), optional: true);
        AddJson(rows, "Configuration", "MES PLC bindings", Path.Combine(_configRoot, "mes-plc-bindings.json"), optional: true);

        var localizationRoot = Path.Combine(_configRoot, "Localization");
        AddJson(rows, "Localization", "cs-CZ", Path.Combine(localizationRoot, "cs-CZ.json"), optional: false);
        AddJson(rows, "Localization", "en-US", Path.Combine(localizationRoot, "en-US.json"), optional: false);
        AddJson(rows, "Localization", "de-DE", Path.Combine(localizationRoot, "de-DE.json"), optional: false);

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        AddValue(rows, "Build", "Application version", version);
        AddFile(rows, "Build", "Running executable", processPath, optional: false);

        ConfigurationGrid.ItemsSource = rows;
        var unavailable = rows.Count(x => x.State == "MISSING" || x.State == "INVALID");
        SummaryText.Text = T(
            "Framework.Runtime.Summary",
            "{0} entries | {1} unavailable",
            rows.Count,
            unavailable);

        _log("FRAMEWORK_RUNTIME_OVERVIEW", $"Entries={rows.Count}; Unavailable={unavailable}");
    }

    private string T(string key, string fallback, params object[] args)
    {
        var format = T(key, fallback);
        try
        {
            return string.Format(format, args);
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddValue(
        ICollection<RuntimeConfigurationRow> rows,
        string category,
        string item,
        string? value)
    {
        rows.Add(new RuntimeConfigurationRow(
            category,
            item,
            string.IsNullOrWhiteSpace(value) ? "—" : value,
            string.IsNullOrWhiteSpace(value) ? "EMPTY" : "ACTIVE",
            string.Empty));
    }

    private static void AddDirectory(
        ICollection<RuntimeConfigurationRow> rows,
        string category,
        string item,
        string path)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        rows.Add(new RuntimeConfigurationRow(
            category,
            item,
            path,
            exists ? "AVAILABLE" : "MISSING",
            exists ? GetDirectoryDetails(path) : "Directory does not exist."));
    }

    private static void AddFile(
        ICollection<RuntimeConfigurationRow> rows,
        string category,
        string item,
        string path,
        bool optional)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        rows.Add(new RuntimeConfigurationRow(
            category,
            item,
            path,
            exists ? "AVAILABLE" : optional ? "OPTIONAL" : "MISSING",
            exists ? GetFileDetails(path) : optional ? "Optional file is not available." : "Required file does not exist."));
    }

    private static void AddJson(
        ICollection<RuntimeConfigurationRow> rows,
        string category,
        string item,
        string path,
        bool optional)
    {
        if (!File.Exists(path))
        {
            rows.Add(new RuntimeConfigurationRow(
                category,
                item,
                path,
                optional ? "OPTIONAL" : "MISSING",
                optional ? "Optional JSON is not available." : "Required JSON does not exist."));
            return;
        }

        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var count = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetArrayLength(),
                JsonValueKind.Object => document.RootElement.EnumerateObject().Count(),
                _ => 1
            };

            rows.Add(new RuntimeConfigurationRow(
                category,
                item,
                path,
                "VALID",
                $"{GetFileDetails(path)}; root={document.RootElement.ValueKind}; entries={count}."));
        }
        catch (Exception ex)
        {
            rows.Add(new RuntimeConfigurationRow(
                category,
                item,
                path,
                "INVALID",
                ex.Message));
        }
    }

    private static string GetFileDetails(string path)
    {
        var info = new FileInfo(path);
        return $"modified={info.LastWriteTime:yyyy-MM-dd HH:mm:ss}; size={info.Length:N0} B";
    }

    private static string GetDirectoryDetails(string path)
    {
        try
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Count();
            var directories = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).Count();
            return $"directories={directories}; files={files}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private sealed record RuntimeConfigurationRow(
        string Category,
        string Item,
        string Value,
        string State,
        string Details);
}
