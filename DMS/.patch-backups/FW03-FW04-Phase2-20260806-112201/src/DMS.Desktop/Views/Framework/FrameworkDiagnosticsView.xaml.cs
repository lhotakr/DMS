using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkDiagnosticsView : UserControl
{
    private readonly string _configRoot;
    private readonly string _dataRoot;
    private readonly Func<string, string> _translate;
    private readonly Action<string, string> _log;

    public FrameworkDiagnosticsView(
        string configRoot,
        string dataRoot,
        Func<string, string> translate,
        Action<string, string> log)
    {
        InitializeComponent();
        _configRoot = configRoot;
        _dataRoot = dataRoot;
        _translate = translate;
        _log = log;

        TitleText.Text = T("Framework.FW04.Title", "FW04 — System diagnostics");
        SubtitleText.Text = T("Framework.FW04.Description", "Validates the configuration, localization, master data and operational paths before deployment.");
        RunButton.Content = T("Framework.Diagnostics.Run", "Run diagnostics");
        FooterText.Text = T("Framework.Diagnostics.Footer", "This check does not modify business data. The write test creates and immediately removes a temporary file.");

        Loaded += (_, _) => RunDiagnostics();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void RunButton_Click(object sender, RoutedEventArgs e) => RunDiagnostics();

    private void RunDiagnostics()
    {
        var rows = new List<DiagnosticRow>();

        CheckDirectory(rows, "Configuration root", _configRoot, requireWrite: true);
        CheckDirectory(rows, "Data root", _dataRoot, requireWrite: true);

        CheckJson(rows, "Transactions", Path.Combine(_configRoot, "transactions.json"));
        CheckJson(rows, "Modules", Path.Combine(_configRoot, "dms-modules.json"));
        CheckJson(rows, "Roles", Path.Combine(_configRoot, "dms-roles.json"));
        CheckJson(rows, "Users", Path.Combine(_configRoot, "users.json"));
        CheckJson(rows, "System settings", Path.Combine(_configRoot, "dms-system-settings.json"));

        var localizationRoot = Path.Combine(_configRoot, "Localization");
        CheckJson(rows, "Localization cs-CZ", Path.Combine(localizationRoot, "cs-CZ.json"));
        CheckJson(rows, "Localization en-US", Path.Combine(localizationRoot, "en-US.json"));
        CheckJson(rows, "Localization de-DE", Path.Combine(localizationRoot, "de-DE.json"));

        CheckOptionalJson(rows, "Checklist definitions", Path.Combine(_dataRoot, "Data", "Checklists", "Definitions"));
        CheckOptionalJson(rows, "Checklist catalogs", Path.Combine(_dataRoot, "Data", "Checklists", "Configuration", "catalogs.json"));
        CheckOptionalJson(rows, "Organization units", Path.Combine(_dataRoot, "Data", "MasterData", "organization-units.json"));
        CheckOptionalJson(rows, "People", Path.Combine(_dataRoot, "Data", "MasterData", "people.json"));
        CheckOptionalJson(rows, "Units", Path.Combine(_dataRoot, "Data", "MasterData", "units.json"));
        CheckOptionalJson(rows, "MES PLC bindings", Path.Combine(_configRoot, "mes-plc-bindings.json"));
        CheckOptionalJson(rows, "MES integration", Path.Combine(_configRoot, "mes-integration.json"));

        ResultsGrid.ItemsSource = rows;
        var errors = rows.Count(x => x.Status == "ERROR");
        var warnings = rows.Count(x => x.Status == "WARNING");
        SummaryText.Text = $"{rows.Count} checks | {errors} errors | {warnings} warnings";

        _log("FRAMEWORK_DIAGNOSTICS", $"Checks={rows.Count}; Errors={errors}; Warnings={warnings}");
    }

    private static void CheckDirectory(List<DiagnosticRow> rows, string name, string path, bool requireWrite)
    {
        if (!Directory.Exists(path))
        {
            rows.Add(new DiagnosticRow("ERROR", name, "Directory does not exist.", path));
            return;
        }

        if (!requireWrite)
        {
            rows.Add(new DiagnosticRow("OK", name, "Directory is available.", path));
            return;
        }

        var probe = Path.Combine(path, $".dms-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "DMS write test");
            File.Delete(probe);
            rows.Add(new DiagnosticRow("OK", name, "Directory exists and is writable.", path));
        }
        catch (Exception ex)
        {
            rows.Add(new DiagnosticRow("ERROR", name, $"Directory is not writable: {ex.Message}", path));
        }
    }

    private static void CheckJson(List<DiagnosticRow> rows, string name, string path)
    {
        if (!File.Exists(path))
        {
            rows.Add(new DiagnosticRow("ERROR", name, "Required JSON file does not exist.", path));
            return;
        }

        ValidateJson(rows, name, path, optional: false);
    }

    private static void CheckOptionalJson(List<DiagnosticRow> rows, string name, string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0)
            {
                rows.Add(new DiagnosticRow("WARNING", name, "Directory exists but contains no JSON files.", path));
                return;
            }

            var invalid = 0;
            foreach (var file in files)
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                }
                catch
                {
                    invalid++;
                }
            }

            rows.Add(invalid == 0
                ? new DiagnosticRow("OK", name, $"{files.Count} JSON file(s) are valid.", path)
                : new DiagnosticRow("ERROR", name, $"{invalid} of {files.Count} JSON file(s) are invalid.", path));
            return;
        }

        if (!File.Exists(path))
        {
            rows.Add(new DiagnosticRow("WARNING", name, "Optional data is not available yet.", path));
            return;
        }

        ValidateJson(rows, name, path, optional: true);
    }

    private static void ValidateJson(List<DiagnosticRow> rows, string name, string path, bool optional)
    {
        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var modified = File.GetLastWriteTime(path);
            rows.Add(new DiagnosticRow("OK", name, $"Valid JSON; modified {modified:yyyy-MM-dd HH:mm:ss}.", path));
        }
        catch (Exception ex)
        {
            rows.Add(new DiagnosticRow(optional ? "WARNING" : "ERROR", name, $"Invalid JSON: {ex.Message}", path));
        }
    }

    private sealed record DiagnosticRow(string Status, string Name, string Details, string Source);
}
