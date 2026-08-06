using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
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
    private readonly List<DiagnosticRow> _allRows = new();

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
        SubtitleText.Text = T(
            "Framework.FW04.Description",
            "Validates configuration integrity, localization parity, master data, checklist definitions and operational paths before deployment.");
        RunButton.Content = T("Framework.Diagnostics.Run", "Run diagnostics");
        CopyButton.Content = T("Framework.Diagnostics.Copy", "Copy report");
        FooterText.Text = T(
            "Framework.Diagnostics.Footer",
            "Diagnostics do not modify business data. Write tests create and immediately remove temporary files.");

        StatusColumn.Header = T("Framework.Diagnostics.Column.Status", "Status");
        CategoryColumn.Header = T("Framework.Diagnostics.Column.Category", "Category");
        NameColumn.Header = T("Framework.Diagnostics.Column.Check", "Check");
        DetailsColumn.Header = T("Framework.Diagnostics.Column.Details", "Details");
        SourceColumn.Header = T("Framework.Diagnostics.Column.Source", "Source");

        StatusFilter.Items.Add(T("Framework.Diagnostics.Filter.All", "All results"));
        StatusFilter.Items.Add("ERROR");
        StatusFilter.Items.Add("WARNING");
        StatusFilter.Items.Add("OK");
        StatusFilter.SelectedIndex = 0;

        Loaded += (_, _) => RunDiagnostics();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
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
            return string.Format(fallback, args);
        }
    }

    private void RunButton_Click(object sender, RoutedEventArgs e) => RunDiagnostics();

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var builder = new StringBuilder();
        builder.AppendLine(TitleText.Text);
        builder.AppendLine(SummaryText.Text);
        builder.AppendLine(new string('-', 90));

        foreach (var row in _allRows)
        {
            builder.AppendLine($"{row.Status}\t{row.Category}\t{row.Name}\t{row.Details}\t{row.Source}");
        }

        Clipboard.SetText(builder.ToString());
        _log("FRAMEWORK_DIAGNOSTICS_COPY", $"Rows={_allRows.Count}");
    }

    private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (ResultsGrid is null || StatusFilter?.SelectedItem is not string selected)
        {
            return;
        }

        ResultsGrid.ItemsSource = selected is "ERROR" or "WARNING" or "OK"
            ? _allRows.Where(x => x.Status == selected).ToList()
            : _allRows.ToList();
    }

    private void RunDiagnostics()
    {
        _allRows.Clear();

        CheckRuntime(_allRows);
        CheckDirectory(_allRows, "Paths", "Configuration root", _configRoot, requireWrite: true);
        CheckDirectory(_allRows, "Paths", "Data root", _dataRoot, requireWrite: true);

        var transactionsPath = Path.Combine(_configRoot, "transactions.json");
        var modulesPath = Path.Combine(_configRoot, "dms-modules.json");
        var rolesPath = Path.Combine(_configRoot, "dms-roles.json");
        var usersPath = Path.Combine(_configRoot, "users.json");

        CheckJson(_allRows, "Configuration", "Transactions", transactionsPath);
        CheckJson(_allRows, "Configuration", "Modules", modulesPath);
        CheckJson(_allRows, "Configuration", "Roles", rolesPath);
        CheckJson(_allRows, "Configuration", "Users", usersPath);
        CheckOptionalJson(_allRows, "Configuration", "System settings", Path.Combine(_configRoot, "dms-system-settings.json"));

        CheckTransactionModuleConsistency(_allRows, transactionsPath, modulesPath);
        CheckRoleReferences(_allRows, transactionsPath, rolesPath);
        CheckUserRoleReferences(_allRows, usersPath, rolesPath);

        var localizationRoot = Path.Combine(_configRoot, "Localization");
        var csPath = Path.Combine(localizationRoot, "cs-CZ.json");
        var enPath = Path.Combine(localizationRoot, "en-US.json");
        var dePath = Path.Combine(localizationRoot, "de-DE.json");

        CheckJson(_allRows, "Localization", "Localization cs-CZ", csPath);
        CheckJson(_allRows, "Localization", "Localization en-US", enPath);
        CheckJson(_allRows, "Localization", "Localization de-DE", dePath);
        CheckLocalizationParity(_allRows, csPath, enPath, dePath);
        CheckTransactionLocalization(_allRows, transactionsPath, csPath, enPath, dePath);
        CheckModuleLocalization(_allRows, modulesPath, csPath, enPath, dePath);

        var checklistRoot = Path.Combine(_dataRoot, "Data", "Checklists");
        var definitionsRoot = Path.Combine(checklistRoot, "Definitions");
        var catalogsPath = Path.Combine(checklistRoot, "Configuration", "catalogs.json");
        CheckOptionalJson(_allRows, "Checklists", "Checklist definitions", definitionsRoot);
        CheckOptionalJson(_allRows, "Checklists", "Checklist catalogs", catalogsPath);
        CheckChecklistDefinitions(_allRows, definitionsRoot, catalogsPath);

        var masterDataRoot = Path.Combine(_dataRoot, "Data", "MasterData");
        var organizationPath = Path.Combine(masterDataRoot, "organization-units.json");
        var peoplePath = Path.Combine(masterDataRoot, "people.json");
        var unitsPath = Path.Combine(masterDataRoot, "units.json");
        CheckOptionalJson(_allRows, "Master data", "Organization units", organizationPath);
        CheckOptionalJson(_allRows, "Master data", "People", peoplePath);
        CheckOptionalJson(_allRows, "Master data", "Units", unitsPath);
        CheckUniqueProperty(_allRows, "Master data", "Organization unit codes", organizationPath, "code");
        CheckUniqueProperty(_allRows, "Master data", "Personnel numbers", peoplePath, "personnelNumber");
        CheckUniqueProperty(_allRows, "Master data", "Unit codes", unitsPath, "code");

        CheckOptionalJson(_allRows, "MES", "MES PLC bindings", Path.Combine(_configRoot, "mes-plc-bindings.json"));
        CheckOptionalJson(_allRows, "MES", "MES integration", Path.Combine(_configRoot, "mes-integration.json"));

        CheckBackups(_allRows, "Backups", "Configuration backups", _configRoot);
        CheckEmergencyLog(_allRows);

        ApplyFilter();

        var errors = _allRows.Count(x => x.Status == "ERROR");
        var warnings = _allRows.Count(x => x.Status == "WARNING");
        var ok = _allRows.Count(x => x.Status == "OK");
        SummaryText.Text = T(
            "Framework.Diagnostics.Summary",
            "{0} checks | {1} errors | {2} warnings | {3} OK",
            _allRows.Count,
            errors,
            warnings,
            ok);

        _log(
            "FRAMEWORK_DIAGNOSTICS",
            $"Checks={_allRows.Count}; Errors={errors}; Warnings={warnings}; Ok={ok}");
    }

    private static void CheckRuntime(ICollection<DiagnosticRow> rows)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        rows.Add(new DiagnosticRow(
            "OK",
            "Runtime",
            "Application build",
            $"Version={assembly.GetName().Version}; Framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
            Environment.ProcessPath ?? string.Empty));
    }

    private static void CheckDirectory(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        string path,
        bool requireWrite)
    {
        if (!Directory.Exists(path))
        {
            rows.Add(new DiagnosticRow("ERROR", category, name, "Directory does not exist.", path));
            return;
        }

        if (!requireWrite)
        {
            rows.Add(new DiagnosticRow("OK", category, name, "Directory is available.", path));
            return;
        }

        var probe = Path.Combine(path, $".dms-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "DMS write test");
            File.Delete(probe);
            rows.Add(new DiagnosticRow("OK", category, name, "Directory exists and is writable.", path));
        }
        catch (Exception ex)
        {
            rows.Add(new DiagnosticRow("ERROR", category, name, $"Directory is not writable: {ex.Message}", path));
        }
    }

    private static void CheckJson(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        string path)
    {
        if (!File.Exists(path))
        {
            rows.Add(new DiagnosticRow("ERROR", category, name, "Required JSON file does not exist.", path));
            return;
        }

        ValidateJson(rows, category, name, path, optional: false);
    }

    private static void CheckOptionalJson(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        string path)
    {
        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0)
            {
                rows.Add(new DiagnosticRow("WARNING", category, name, "Directory exists but contains no JSON files.", path));
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
                ? new DiagnosticRow("OK", category, name, $"{files.Count} JSON file(s) are valid.", path)
                : new DiagnosticRow("ERROR", category, name, $"{invalid} of {files.Count} JSON file(s) are invalid.", path));
            return;
        }

        if (!File.Exists(path))
        {
            rows.Add(new DiagnosticRow("WARNING", category, name, "Optional data is not available yet.", path));
            return;
        }

        ValidateJson(rows, category, name, path, optional: true);
    }

    private static void ValidateJson(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        string path,
        bool optional)
    {
        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var modified = File.GetLastWriteTime(path);
            rows.Add(new DiagnosticRow("OK", category, name, $"Valid JSON; modified {modified:yyyy-MM-dd HH:mm:ss}.", path));
        }
        catch (Exception ex)
        {
            rows.Add(new DiagnosticRow(optional ? "WARNING" : "ERROR", category, name, $"Invalid JSON: {ex.Message}", path));
        }
    }

    private static void CheckTransactionModuleConsistency(
        ICollection<DiagnosticRow> rows,
        string transactionsPath,
        string modulesPath)
    {
        var transactionsLoaded = TryReadArray(
            transactionsPath,
            out var transactions,
            out var transactionError);

        var modulesLoaded = TryReadArray(
            modulesPath,
            out var modules,
            out var moduleError);

        if (!transactionsLoaded || !modulesLoaded)
        {
            rows.Add(new DiagnosticRow(
                "WARNING",
                "Cross-reference",
                "Transaction to module mapping",
                transactionError
                ?? moduleError
                ?? "Configuration could not be evaluated.",
                transactionsPath));

            return;
        }

        var moduleCodes = modules
            .Select(x => GetString(x, "Code", "code"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var transactionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateTransactions = new List<string>();
        var missingModules = new List<string>();

        foreach (var transaction in transactions)
        {
            var code = GetString(transaction, "Code", "code");
            var module = GetString(transaction, "Module", "module");
            if (!string.IsNullOrWhiteSpace(code) && !transactionCodes.Add(code))
            {
                duplicateTransactions.Add(code);
            }

            if (!string.IsNullOrWhiteSpace(module) && !moduleCodes.Contains(module))
            {
                missingModules.Add($"{code}->{module}");
            }
        }

        AddListResult(rows, "Cross-reference", "Duplicate transaction codes", duplicateTransactions, transactionsPath);
        AddListResult(rows, "Cross-reference", "Transactions referencing unknown modules", missingModules, modulesPath);
    }

    private static void CheckRoleReferences(
        ICollection<DiagnosticRow> rows,
        string transactionsPath,
        string rolesPath)
    {
        if (!TryReadArray(transactionsPath, out var transactions, out _) ||
            !TryReadArray(rolesPath, out var roles, out _))
        {
            return;
        }

        var knownRoles = roles
            .Select(x => GetString(x, "Code", "code", "RoleCode", "roleCode"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var transaction in transactions)
        {
            var code = GetString(transaction, "Code", "code");
            foreach (var role in GetStringArray(transaction, "Roles", "roles"))
            {
                if (!knownRoles.Contains(role))
                {
                    missing.Add($"{code}->{role}");
                }
            }
        }

        AddListResult(rows, "Cross-reference", "Transaction roles", missing, rolesPath);
    }

    private static void CheckUserRoleReferences(
        ICollection<DiagnosticRow> rows,
        string usersPath,
        string rolesPath)
    {
        if (!TryReadArray(usersPath, out var users, out _) ||
            !TryReadArray(rolesPath, out var roles, out _))
        {
            return;
        }

        var knownRoles = roles
            .Select(x => GetString(x, "Code", "code", "RoleCode", "roleCode"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var user in users)
        {
            var login = GetString(user, "WindowsLogin", "windowsLogin", "Login", "login");
            foreach (var role in GetStringArray(user, "Roles", "roles"))
            {
                if (!knownRoles.Contains(role))
                {
                    missing.Add($"{login}->{role}");
                }
            }
        }

        AddListResult(rows, "Cross-reference", "User roles", missing, usersPath);
    }

    private static void CheckLocalizationParity(
        ICollection<DiagnosticRow> rows,
        string csPath,
        string enPath,
        string dePath)
    {
        if (!TryReadObjectKeys(csPath, out var cs, out _) ||
            !TryReadObjectKeys(enPath, out var en, out _) ||
            !TryReadObjectKeys(dePath, out var de, out _))
        {
            return;
        }

        var union = cs.Union(en, StringComparer.OrdinalIgnoreCase)
            .Union(de, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AddLocalizationMissing(rows, "en-US", union.Except(en, StringComparer.OrdinalIgnoreCase), enPath);
        AddLocalizationMissing(rows, "de-DE", union.Except(de, StringComparer.OrdinalIgnoreCase), dePath);
        AddLocalizationMissing(rows, "cs-CZ", union.Except(cs, StringComparer.OrdinalIgnoreCase), csPath);
    }

    private static void CheckTransactionLocalization(
        ICollection<DiagnosticRow> rows,
        string transactionsPath,
        params string[] localizationPaths)
    {
        if (!TryReadArray(transactionsPath, out var transactions, out _))
        {
            return;
        }

        var expected = transactions
            .SelectMany(x =>
            {
                var code = GetString(x, "Code", "code");
                return string.IsNullOrWhiteSpace(code)
                    ? Array.Empty<string>()
                    : new[] { $"Transaction.{code}.Name", $"Transaction.{code}.Description" };
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in localizationPaths)
        {
            if (!TryReadObjectKeys(path, out var keys, out _))
            {
                continue;
            }

            var missing = expected.Except(keys, StringComparer.OrdinalIgnoreCase).ToList();
            var culture = Path.GetFileNameWithoutExtension(path);
            AddListResult(rows, "Localization", $"Transaction localization {culture}", missing, path, warning: true);
        }
    }

    private static void CheckModuleLocalization(
        ICollection<DiagnosticRow> rows,
        string modulesPath,
        params string[] localizationPaths)
    {
        if (!TryReadArray(modulesPath, out var modules, out _))
        {
            return;
        }

        var expected = modules
            .SelectMany(x =>
            {
                var code = GetString(x, "Code", "code");
                return string.IsNullOrWhiteSpace(code)
                    ? Array.Empty<string>()
                    : new[] { $"Module.{code}.Name", $"Module.{code}.Description" };
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in localizationPaths)
        {
            if (!TryReadObjectKeys(path, out var keys, out _))
            {
                continue;
            }

            var missing = expected.Except(keys, StringComparer.OrdinalIgnoreCase).ToList();
            var culture = Path.GetFileNameWithoutExtension(path);
            AddListResult(rows, "Localization", $"Module localization {culture}", missing, path, warning: true);
        }
    }

    private static void CheckChecklistDefinitions(
        ICollection<DiagnosticRow> rows,
        string definitionsRoot,
        string catalogsPath)
    {
        if (!Directory.Exists(definitionsRoot))
        {
            return;
        }

        var catalogCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (TryReadArray(catalogsPath, out var catalogs, out _))
        {
            foreach (var catalog in catalogs)
            {
                var code = GetString(catalog, "Code", "code");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    catalogCodes.Add(code);
                }
            }
        }

        var definitionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateDefinitionCodes = new List<string>();
        var duplicateFieldCodes = new List<string>();
        var missingCatalogs = new List<string>();

        foreach (var file in Directory.EnumerateFiles(definitionsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;
                var definitionCode = GetString(root, "Code", "code");
                if (!string.IsNullOrWhiteSpace(definitionCode) && !definitionCodes.Add(definitionCode))
                {
                    duplicateDefinitionCodes.Add(definitionCode);
                }

                var fieldCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                WalkChecklistElement(root, definitionCode, fieldCodes, duplicateFieldCodes, catalogCodes, missingCatalogs);
            }
            catch
            {
                // Invalid JSON is already reported by the generic check.
            }
        }

        AddListResult(rows, "Checklists", "Duplicate definition codes", duplicateDefinitionCodes, definitionsRoot);
        AddListResult(rows, "Checklists", "Duplicate field codes", duplicateFieldCodes, definitionsRoot);
        AddListResult(rows, "Checklists", "Unknown checklist catalogs", missingCatalogs, catalogsPath, warning: true);
    }

    private static void WalkChecklistElement(
        JsonElement element,
        string definitionCode,
        ISet<string> fieldCodes,
        ICollection<string> duplicateFieldCodes,
        ISet<string> catalogCodes,
        ICollection<string> missingCatalogs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var code = GetString(element, "FieldCode", "fieldCode", "Code", "code");
            var fieldType = GetString(element, "FieldType", "fieldType");
            if (!string.IsNullOrWhiteSpace(fieldType) &&
                !string.IsNullOrWhiteSpace(code) &&
                !fieldCodes.Add(code))
            {
                duplicateFieldCodes.Add($"{definitionCode}:{code}");
            }

            var catalogCode = GetString(element, "CatalogCode", "catalogCode");
            if (!string.IsNullOrWhiteSpace(catalogCode) &&
                catalogCodes.Count > 0 &&
                !catalogCodes.Contains(catalogCode))
            {
                missingCatalogs.Add($"{definitionCode}:{code}->{catalogCode}");
            }

            foreach (var property in element.EnumerateObject())
            {
                WalkChecklistElement(
                    property.Value,
                    definitionCode,
                    fieldCodes,
                    duplicateFieldCodes,
                    catalogCodes,
                    missingCatalogs);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                WalkChecklistElement(
                    item,
                    definitionCode,
                    fieldCodes,
                    duplicateFieldCodes,
                    catalogCodes,
                    missingCatalogs);
            }
        }
    }

    private static void CheckUniqueProperty(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        string path,
        params string[] propertyNames)
    {
        if (!TryReadArray(path, out var items, out _))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var item in items)
        {
            var value = GetString(item, propertyNames);
            if (!string.IsNullOrWhiteSpace(value) && !seen.Add(value))
            {
                duplicates.Add(value);
            }
        }

        AddListResult(rows, category, name, duplicates, path);
    }

    private static void CheckBackups(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var backups = Directory
            .EnumerateFiles(root, "*.bak-*", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(root, "*.broken-*", SearchOption.TopDirectoryOnly))
            .ToList();

        rows.Add(backups.Count > 0
            ? new DiagnosticRow("OK", category, name, $"{backups.Count} backup file(s) found; newest={backups.Max(File.GetLastWriteTime):yyyy-MM-dd HH:mm:ss}.", root)
            : new DiagnosticRow("WARNING", category, name, "No timestamped configuration backup was found.", root));
    }

    private static void CheckEmergencyLog(ICollection<DiagnosticRow> rows)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DMS",
            "Logs",
            "dms-emergency.log");

        if (!File.Exists(path))
        {
            rows.Add(new DiagnosticRow(
                "OK",
                "Runtime",
                "Emergency log",
                "No emergency log exists; no globally captured failure has been recorded on this client.",
                path));
            return;
        }

        var info = new FileInfo(path);
        rows.Add(new DiagnosticRow(
            info.Length == 0 ? "OK" : "WARNING",
            "Runtime",
            "Emergency log",
            $"size={info.Length:N0} B; modified={info.LastWriteTime:yyyy-MM-dd HH:mm:ss}. Review before production deployment.",
            path));
    }

    private static void AddLocalizationMissing(
        ICollection<DiagnosticRow> rows,
        string culture,
        IEnumerable<string> missing,
        string path)
    {
        AddListResult(
            rows,
            "Localization",
            $"Localization parity {culture}",
            missing.ToList(),
            path,
            warning: true);
    }

    private static void AddListResult(
        ICollection<DiagnosticRow> rows,
        string category,
        string name,
        IReadOnlyCollection<string> problems,
        string source,
        bool warning = false)
    {
        if (problems.Count == 0)
        {
            rows.Add(new DiagnosticRow("OK", category, name, "No problem found.", source));
            return;
        }

        var sample = string.Join(", ", problems.Take(20));
        if (problems.Count > 20)
        {
            sample += $", … (+{problems.Count - 20})";
        }

        rows.Add(new DiagnosticRow(
            warning ? "WARNING" : "ERROR",
            category,
            name,
            $"{problems.Count} problem(s): {sample}",
            source));
    }

    private static bool TryReadArray(
        string path,
        out List<JsonElement> items,
        out string? error)
    {
        items = new List<JsonElement>();
        error = null;

        try
        {
            if (!File.Exists(path))
            {
                error = "File does not exist.";
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                items = root.EnumerateArray().Select(x => x.Clone()).ToList();
                return true;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "items", "Items", "transactions", "Transactions", "modules", "Modules", "roles", "Roles", "users", "Users", "catalogs", "Catalogs" })
                {
                    if (root.TryGetProperty(propertyName, out var property) &&
                        property.ValueKind == JsonValueKind.Array)
                    {
                        items = property.EnumerateArray().Select(x => x.Clone()).ToList();
                        return true;
                    }
                }
            }

            error = $"Expected a JSON array, got {root.ValueKind}.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadObjectKeys(
        string path,
        out HashSet<string> keys,
        out string? error)
    {
        keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        try
        {
            if (!File.Exists(path))
            {
                error = "File does not exist.";
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Localization root must be a JSON object.";
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                keys.Add(property.Name);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    _ => string.Empty
                };
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetStringArray(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)) ||
                property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }
    }

    private sealed record DiagnosticRow(
        string Status,
        string Category,
        string Name,
        string Details,
        string Source);
}
