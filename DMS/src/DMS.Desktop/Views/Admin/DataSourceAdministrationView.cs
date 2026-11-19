using ClosedXML.Excel;
using DMS.Desktop.Logging;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Definitions;
using Microsoft.Data.SqlClient;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Admin;

/// <summary>SYS15: data source metadata, bounded SQL override editor and audit trail.</summary>
public sealed class DataSourceAdministrationView : UserControl
{
    private readonly string _configRoot;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;
    private readonly DmsDataSourceCatalog _sources;
    private readonly string _sqlPath;
    private readonly ObservableCollection<DmsDataSourceEntry> _rows = new();
    private readonly DataGrid _sourceGrid = new() { AutoGenerateColumns = false, CanUserAddRows = false,
        MinHeight = 180, IsReadOnly = false, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBox _metadata = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"),
        TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 150 };
    private readonly ComboBox _queryChoice = new() { Width = 210, Margin = new Thickness(0,0,8,0) };
    private readonly TextBox _sql = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12,
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 280 };
    private readonly TextBlock _sqlStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sourceStatus = new() { TextWrapping = TextWrapping.Wrap };
    private string _validatedCode = string.Empty;
    private string _validatedSql = string.Empty;
    private Button _activate = null!;

    public DataSourceAdministrationView(string configRoot, DmsLogger logger, string user, Func<string,string> translate)
    {
        _configRoot = configRoot;
        _logger = logger;
        _user = user;
        _translate = translate;
        _sources = new DmsDataSourceCatalog(configRoot);
        _sqlPath = MesSqlOverrideCatalog.FilePath(configRoot);

        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var title = new TextBlock { Text = T("SYS15.Title", "SYS15 – Správa datových zdrojů a dotazů"),
            FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var tabs = new TabControl();
        root.Children.Add(tabs);
        tabs.Items.Add(new TabItem { Header = T("SYS15.Tab.Sources", "Datové zdroje"), Content = BuildSources() });
        tabs.Items.Add(new TabItem { Header = T("SYS15.Tab.Sql", "Výchozí SQL dotazy MES06"), Content = BuildQueries() });
        ReloadSources();
        _queryChoice.SelectedIndex = 0;
        _logger.AdminAction("SYS15", "DATA_CATALOG_OPEN", _user, "Catalog and SQL editor opened");
    }

    private string T(string key, string fallback)
    {
        var result = _translate(key);
        return string.IsNullOrWhiteSpace(result) || result == key || result.StartsWith("[[", StringComparison.Ordinal)
            ? fallback : result;
    }

    private UIElement BuildSources()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0,0,0,8) };
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Paragraph(T("SYS15.SourceInfo", "MESSET a interní úložiště jsou systémové zdroje. Pro XLSX, CSV a JSON lze registrovat cestu k souboru. Registrace ještě nespouští žádný import.")));
        var actions = new WrapPanel(); top.Children.Add(actions);
        actions.Children.Add(Action(T("SYS15.AddFile", "Přidat soubor"), (_,_) => AddSource()));
        actions.Children.Add(Action(T("SYS15.Remove", "Odebrat"), (_,_) => RemoveSource()));
        actions.Children.Add(Action(T("SYS15.Save", "Uložit zdroje"), (_,_) => SaveSources()));
        actions.Children.Add(Action(T("SYS15.Inspect", "Načíst strukturu"), async (_,_) => await InspectSelectedAsync()));
        actions.Children.Add(Action(T("SYS15.Reload", "Obnovit"), (_,_) => ReloadSources()));
        top.Children.Add(_sourceStatus);
        _sourceGrid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Column.Code", "Kód"), Binding = new Binding(nameof(DmsDataSourceEntry.Code)), Width = new DataGridLength(100) });
        _sourceGrid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Column.Name", "Název"), Binding = new Binding(nameof(DmsDataSourceEntry.Name)), Width = new DataGridLength(205) });
        _sourceGrid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Column.Kind", "Typ"), Binding = new Binding(nameof(DmsDataSourceEntry.Kind)), Width = new DataGridLength(90) });
        _sourceGrid.Columns.Add(new DataGridTextColumn { Header = T("SYS15.Column.Location", "Cesta / nastavení"), Binding = new Binding(nameof(DmsDataSourceEntry.Location)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _sourceGrid.Columns.Add(new DataGridCheckBoxColumn { Header = T("SYS15.Column.Enabled", "Aktivní"), Binding = new Binding(nameof(DmsDataSourceEntry.Enabled)), Width = new DataGridLength(80) });
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(_sourceGrid);
        var heading = Paragraph(T("SYS15.Metadata", "Struktura vybraného zdroje (pouze pro čtení)"));
        heading.Margin = new Thickness(0,8,0,8); Grid.SetRow(heading, 1); body.Children.Add(heading);
        Grid.SetRow(_metadata, 2); body.Children.Add(_metadata);
        panel.Children.Add(body);
        return panel;
    }

    private UIElement BuildQueries()
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel { Margin = new Thickness(0,0,0,8) }; DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(Paragraph(T("SYS15.SqlInfo", "Výchozí dotaz zůstává v aplikaci. Úprava je samostatná konfigurace. Změní pouze interaktivní sestavy MES06: Výroba, Stavy, Čítače. Po úpravě je vyžadován živý test schématu SQL.")));
        var actions = new WrapPanel(); top.Children.Add(actions);
        foreach (var code in MesSqlOverrideCatalog.ColumnCounts.Keys) _queryChoice.Items.Add(code);
        _queryChoice.SelectionChanged += (_,_) => ShowQuery();
        actions.Children.Add(_queryChoice);
        actions.Children.Add(Action(T("SYS15.SqlDefault", "Výchozí z kódu"), (_,_) => { if (_queryChoice.SelectedItem is string c) _sql.Text = MesSqlOverrideCatalog.BuiltIn(c); }));
        actions.Children.Add(Action(T("SYS15.SqlTest", "Ověřit SQL a sloupce"), async (_,_) => await CheckQueryAsync()));
        _activate = Action(T("SYS15.SqlActivate", "Aktivovat úpravu"), (_,_) => ActivateQuery());
        _activate.IsEnabled = false;
        actions.Children.Add(_activate);
        actions.Children.Add(Action(T("SYS15.SqlReset", "Vrátit výchozí"), (_,_) => ResetQuery()));
        _sql.TextChanged += (_,_) => { _validatedCode = _validatedSql = ""; if (_activate != null) _activate.IsEnabled = false; };
        top.Children.Add(_sqlStatus);
        panel.Children.Add(_sql);
        return panel;
    }

    private static TextBlock Paragraph(string text) => new()
    { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,9) };

    private static Button Action(string title, RoutedEventHandler handler)
    {
        var button = new Button { Content = title, Padding = new Thickness(12,6,12,6),
            Margin = new Thickness(0,0,8,8), MinWidth = 110 };
        button.Click += handler;
        return button;
    }

    private void ReloadSources()
    {
        _rows.Clear(); foreach (var item in _sources.Load()) _rows.Add(item);
        _sourceGrid.ItemsSource = _rows;
        _sourceStatus.Text = T("SYS15.SourcesLoaded", "Zdroje načteny.");
    }

    private void AddSource()
    {
        var code = "FILE_" + (1 + _rows.Count(x => x.Code.StartsWith("FILE_", StringComparison.OrdinalIgnoreCase)));
        var row = new DmsDataSourceEntry { Code = code, Name = "New file source", Kind = "XLSX" };
        _rows.Add(row); _sourceGrid.SelectedItem = row;
    }

    private void RemoveSource()
    {
        _sourceGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_sourceGrid.SelectedItem is not DmsDataSourceEntry item) return;
        if (item.Code is "MES" or "SAP_CACHE" or "QUALITY")
        { _sourceStatus.Text = T("SYS15.BuiltinLocked", "Systémový zdroj nelze odstranit."); return; }
        _rows.Remove(item);
    }

    private void SaveSources()
    {
        try
        {
            _sourceGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _sourceGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var before = _sources.Load().ToDictionary(x => x.Code, x => x, StringComparer.OrdinalIgnoreCase);
            _sources.Save(_rows);
            foreach (var row in _rows)
            {
                if (!before.TryGetValue(row.Code, out var original))
                    _logger.AuditCreated("SYS15", "DataSource", row.Code, _user, $"Kind={row.Kind}; Location={row.Location}");
                else if (original.Name != row.Name || original.Kind != row.Kind || original.Location != row.Location || original.Enabled != row.Enabled)
                    _logger.AuditChange("SYS15", "DataSource", row.Code, "Metadata",
                        $"Name={original.Name}; Kind={original.Kind}; Location={original.Location}; Enabled={original.Enabled}",
                        $"Name={row.Name}; Kind={row.Kind}; Location={row.Location}; Enabled={row.Enabled}", _user);
            }
            foreach (var entry in before.Keys.Where(x => _rows.All(y => !y.Code.Equals(x, StringComparison.OrdinalIgnoreCase))))
                _logger.AuditDeleted("SYS15", "DataSource", entry, _user, "Data source deregistered");
            _sourceStatus.Text = T("SYS15.Saved", "Katalog uložen.");
        }
        catch (Exception ex) { _sourceStatus.Text = ex.Message; _logger.Error("SYS15 data source save failed", ex); }
    }

    private async Task InspectSelectedAsync()
    {
        if (_sourceGrid.SelectedItem is not DmsDataSourceEntry item) return;
        _metadata.Text = T("SYS15.Inspecting", "Načítám strukturu…");
        try
        {
            if (item.Kind == "MES_SQL")
            {
                var settings = ReadMesSettings();
                if (!settings.IsEnabled) throw new InvalidOperationException("MESSET is disabled.");
                await using var connection = await new MesSqlConnectionFactory().OpenAsync(settings);
                await using var command = connection.CreateCommand();
                command.CommandTimeout = Math.Min(settings.CommandTimeoutSeconds, 20);
                command.CommandText = """
SELECT TOP (300) sch.name, obj.name, col.name, typ.name
FROM sys.objects AS obj
JOIN sys.schemas AS sch ON sch.schema_id = obj.schema_id
JOIN sys.columns AS col ON col.object_id = obj.object_id
JOIN sys.types AS typ ON typ.user_type_id = col.user_type_id
WHERE obj.type IN ('U','V') AND obj.is_ms_shipped = 0
ORDER BY sch.name, obj.name, col.column_id
""";
                var lines = new List<string>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) lines.Add($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)} : {reader.GetString(3)}");
                _metadata.Text = string.Join(Environment.NewLine, lines);
            }
            else if (item.Kind == "XLSX")
            {
                if (new FileInfo(item.Location).Length > 50_000_000)
                    throw new InvalidOperationException("XLSX source exceeds the 50 MB inspection limit.");
                using var workbook = new XLWorkbook(item.Location);
                _metadata.Text = string.Join(Environment.NewLine, workbook.Worksheets.Select(sheet =>
                    sheet.Name + " => " + string.Join(", ", sheet.FirstRowUsed()?.CellsUsed().Take(100).Select(cell => cell.GetString()) ?? Array.Empty<string>())));
            }
            else if (item.Kind == "CSV")
                _metadata.Text = string.Join(" | ", File.ReadLines(item.Location).FirstOrDefault()?.Split(';') ?? Array.Empty<string>());
            else if (item.Kind == "JSON")
            {
                if (new FileInfo(item.Location).Length > 20_000_000)
                    throw new InvalidOperationException("JSON source exceeds the 20 MB inspection limit.");
                using var stream = File.OpenRead(item.Location);
                using var json = JsonDocument.Parse(stream);
                var node = json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0
                    ? json.RootElement[0] : json.RootElement;
                _metadata.Text = node.ValueKind == JsonValueKind.Object
                    ? string.Join(Environment.NewLine, node.EnumerateObject().Select(x => $"{x.Name}: {x.Value.ValueKind}"))
                    : node.ValueKind.ToString();
            }
            else _metadata.Text = T("SYS15.AdapterLater", "Systémový adaptér existuje; prohlížení jeho struktury rozšíříme v další etapě.");
            _logger.AdminAction("SYS15", "SCHEMA_INSPECT", _user, $"Source={item.Code}; Kind={item.Kind}");
        }
        catch (Exception ex) { _metadata.Text = ex.Message; _logger.Error($"SYS15 schema inspection failed: {item.Code}", ex); }
    }

    private MesDatabaseConnectionSettings ReadMesSettings()
    {
        var names = new[]{ "mes-database-settings.json", "mes-reporting-settings.json", "mes-sql-settings.json", "mes-database.json", "mes-reporting.json" };
        var path = names.Select(n => Path.Combine(_configRoot, n)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("MESSET configuration not found.");
        return new MesDatabaseSettingsService().Load(path);
    }

    private void ShowQuery()
    {
        if (_queryChoice.SelectedItem is not string code) return;
        try
        {
            var overrides = MesSqlOverrideCatalog.Load(_sqlPath);
            var custom = overrides.TryGetValue(code, out var sql) && !string.IsNullOrWhiteSpace(sql);
            _sql.Text = custom ? sql! : MesSqlOverrideCatalog.BuiltIn(code);
            _sqlStatus.Text = custom ? T("SYS15.SqlActive", "Aktivní upravený dotaz") : T("SYS15.SqlBuiltin", "Použit původní dotaz z programu");
        }
        catch (Exception ex) { _sqlStatus.Text = ex.Message; _logger.Error("SYS15 SQL template load failed", ex); }
    }

    private async Task CheckQueryAsync()
    {
        if (_queryChoice.SelectedItem is not string code) return;
        var snapshot = _sql.Text;
        try
        {
            MesSqlOverrideCatalog.Validate(snapshot);
            var settings = ReadMesSettings();
            if (!settings.IsEnabled) throw new InvalidOperationException("MESSET is disabled.");
            await using var connection = await new MesSqlConnectionFactory().OpenAsync(settings);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = Math.Min(settings.CommandTimeoutSeconds, 15);
            if (!System.Text.RegularExpressions.Regex.IsMatch(settings.ReportingSchema, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
                throw new InvalidOperationException("Invalid MES reporting schema.");
            command.CommandText = snapshot.Replace("{schema}", settings.ReportingSchema, StringComparison.OrdinalIgnoreCase);
            command.Parameters.AddWithValue("@maxRows", 10);
            command.Parameters.AddWithValue("@from", DateTime.Today.AddDays(-1));
            command.Parameters.AddWithValue("@to", DateTime.Today);
            command.Parameters.AddWithValue("@effectiveTo", DateTime.Today);
            command.Parameters.AddWithValue("@workcenter", string.Empty);
            command.Parameters.AddWithValue("@orderCode", string.Empty);
            command.Parameters.AddWithValue("@productCode", string.Empty);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult);
            var columns = reader.GetColumnSchema();
            if (columns.Count != MesSqlOverrideCatalog.ColumnCounts[code])
                throw new InvalidOperationException($"Incorrect output column count: {columns.Count}, expected {MesSqlOverrideCatalog.ColumnCounts[code]}.");
            var expected = code switch
            {
                "PRODUCTION" => new[]{ "Starttime","Endtime","Code","Description","PlantName","OrderCode","OperationCode","OperationQuantity","ProductCode","ProductDescription","OrderQuantity","PerformanceTotal","PerformanceGood","PerformanceBad","PerformanceRework","DurationUtilization","DurationDown" },
                "STATES" => new[]{ "Code","OrderCode","ProductCode","Starttime","Endtime","Name","Description","CategoryName","IsSetup","IsBreak","IsCauselessFailure","CustomText","DurationSeconds" },
                _ => new[]{ "Code","OrderCode","OperationCode","ProductCode","Name","Description","Timestamp","Value","CustomText" }
            };
            for (var i = 0; i < expected.Length; i++)
                if (!string.Equals(columns[i].ColumnName, expected[i], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Column #{i+1} must be {expected[i]}, received {columns[i].ColumnName}.");
            if (_sql.Text == snapshot && _queryChoice.SelectedItem is string actual && actual == code)
            {
                _validatedCode = code; _validatedSql = snapshot; _activate.IsEnabled = true;
            }
            _sqlStatus.Text = T("SYS15.SqlValid", "Dotaz prošel kontrolou SQL a výsledných sloupců.");
            _logger.AdminAction("SYS15", "SQL_VALIDATE", _user, $"Query={code}; Columns={columns.Count}; ReadOnly=True");
        }
        catch (Exception ex)
        {
            _sqlStatus.Text = ex.Message;
            _logger.Warning($"SYS15 SQL template validation rejected. Query={code}; Reason={ex.Message}");
        }
    }

    private void ActivateQuery()
    {
        if (_queryChoice.SelectedItem is not string code || _validatedCode != code || _validatedSql != _sql.Text)
        { _sqlStatus.Text = T("SYS15.SqlTestFirst", "Po změně dotazu nejprve spusť kontrolu."); return; }
        try
        {
            var values = MesSqlOverrideCatalog.Load(_sqlPath);
            values.TryGetValue(code, out var previous);
            values[code] = _validatedSql;
            MesSqlOverrideCatalog.Save(_sqlPath, values);
            _logger.AuditChange("SYS15", "SqlQuery", code, "SQL_SHA256", Fingerprint(previous), Fingerprint(_validatedSql), _user);
            _logger.AdminAction("SYS15", "SQL_OVERRIDE_ACTIVATE", _user, $"Query={code}; Config={_sqlPath}");
            _sqlStatus.Text = T("SYS15.SqlSaved", "Upravený dotaz uložen. Načte se při dalším spuštění sestavy MES06.");
            _activate.IsEnabled = false;
        }
        catch (Exception ex) { _sqlStatus.Text = ex.Message; _logger.Error("SYS15 SQL template save failed", ex); }
    }

    private static string Fingerprint(string? sql) => string.IsNullOrWhiteSpace(sql) ? "(built-in)" :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

    private void ResetQuery()
    {
        if (_queryChoice.SelectedItem is not string code) return;
        try
        {
            var values = MesSqlOverrideCatalog.Load(_sqlPath);
            if (values.Remove(code, out var previous))
            {
                MesSqlOverrideCatalog.Save(_sqlPath, values);
                _logger.AuditDeleted("SYS15", "SqlQueryOverride", code, _user, "Override removed; default restored");
                _logger.AuditChange("SYS15", "SqlQuery", code, "SQL_SHA256", Fingerprint(previous), null, _user);
            }
            ShowQuery();
            _sqlStatus.Text = T("SYS15.SqlRestored", "Obnoven původní dotaz z programu.");
        }
        catch (Exception ex) { _sqlStatus.Text = ex.Message; _logger.Error("SYS15 SQL restore failed", ex); }
    }
}
