using ClosedXML.Excel;
using DMS.Desktop.Logging;
using DMS.Desktop.Services;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting.Definitions;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views.Admin;

/// <summary>
/// SYS15 v0.2. Visual editor of schema metadata and relationships.
/// It NEVER runs a join/query generated from a graph and does not alter MES06 SQL.
/// </summary>
public sealed class Sys15GraphModelDesignerView : UserControl
{
    private readonly string _configRoot;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;
    private readonly DmsGraphModelCatalog _catalog;
    private readonly DmsDataSourceCatalog _sources;
    private readonly Canvas _canvas = new() { Width = 1250, Height = 900 };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScrollBar _horizontalPan = new() { Orientation = Orientation.Horizontal, Height = 23, Minimum = 0,
        SmallChange = 45, LargeChange = 200, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Slider _zoomControl = new() { Minimum = .35, Maximum = 2.25, Value = 1,
        TickFrequency = .05, IsSnapToTickEnabled = false, Width = 150, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 5, 0) };
    private readonly TextBlock _zoomText = new() { Width = 52, VerticalAlignment = VerticalAlignment.Center };
    private ScrollViewer? _graphScroll;
    private double _zoom = 1;
    private bool _updatingPan;
    private Point? _panStart;
    private double _panStartX, _panStartY;
    private readonly ComboBox _modelChoice = new() { Width = 215 };
    private readonly TextBox _name = new() { Width = 190 };
    private readonly ComboBox _sourceChoice = new() { MinWidth = 260 };
    private readonly TextBlock _sourceDetail = new() { TextWrapping = TextWrapping.Wrap, Opacity = .80, Margin = new Thickness(0, 0, 0, 8), FontSize = 11 };
    private readonly ComboBox _tableChoice = new() { MinWidth = 225, DisplayMemberPath = nameof(SchemaTable.Name) };
    private readonly ListBox _relationships = new() { MinHeight = 160, DisplayMemberPath = nameof(RelationChoice.Caption) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selection = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<UIElement> _edgeElements = new();
    private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SchemaTable>> _tableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sourceCacheKeys = new(StringComparer.OrdinalIgnoreCase);
    private List<DmsGraphModelDefinition> _saved = new();
    private DmsGraphModelDefinition _current = new() { Code = "MODEL_01", Name = "Nový model" };
    private string _fingerprint = string.Empty;
    private bool _switching;
    private bool _dirty;
    private (string NodeId, string Column)? _pendingField;
    private string? _selectedNodeId;
    private UIElement? _dragHandle;
    private DmsGraphNode? _dragNode;
    private Point _lastMouse;

    private sealed class SchemaTable
    {
        public string Name { get; init; } = string.Empty;
        public List<DmsGraphColumn> Columns { get; init; } = new();
        public override string ToString() => Name;
    }

    private sealed class RelationChoice
    {
        public DmsGraphRelationship Relationship { get; init; } = new();
        public string Caption { get; init; } = string.Empty;
        public override string ToString() => Caption;
    }

    private sealed record Option(string Value, string Caption)
    {
        public override string ToString() => Caption;
    }

    public Sys15GraphModelDesignerView(string configRoot, DmsLogger logger, string user, Func<string, string> translate)
    {
        _configRoot = configRoot;
        _logger = logger;
        _user = user;
        _translate = translate;
        _catalog = new DmsGraphModelCatalog(configRoot);
        _sources = new DmsDataSourceCatalog(configRoot);
        // The DMS ComboBox template presents the selected object directly. An explicit
        // item template makes names visible even with that custom WPF control template.
        _sourceChoice.ItemTemplate = CreateNameTemplate(nameof(DmsDataSourceEntry.Name), nameof(DmsDataSourceEntry.Location));
        _modelChoice.ItemTemplate = CreateNameTemplate(nameof(DmsGraphModelDefinition.Name));
        BuildUi();
        ReloadModels(false);
        LoadSources();
        // The view is constructed before an administrator can register XLSX in the
        // first SYS15 tab; refresh the catalog when this tab becomes visible.
        IsVisibleChanged += (_, _) => { if (IsVisible) LoadSources(); };
        _logger.AdminAction("SYS15", "GRAPH_EDITOR_OPEN", _user, "Graph model editor opened; metadata only");
    }

    private string T(string key, string fallback)
    {
        var result = _translate(key);
        return string.IsNullOrWhiteSpace(result) || result == key || result.StartsWith("[[", StringComparison.Ordinal)
            ? fallback : result;
    }

    private static DataTemplate CreateNameTemplate(string nameProperty, string? tooltipProperty = null)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameProperty));
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        if (!string.IsNullOrWhiteSpace(tooltipProperty))
            label.SetBinding(FrameworkElement.ToolTipProperty, new Binding(tooltipProperty));
        return new DataTemplate { VisualTree = label };
    }

    private static Button MakeButton(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 6, 7) };
        b.Click += click;
        return b;
    }

    private void BuildUi()
    {
        var layout = new DockPanel { Margin = new Thickness(6) };
        Content = layout;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        top.Children.Add(new TextBlock
        {
            Text = T("SYS15.Graph.Intro", "Vizuální datový model: načti sloupce, vlož tabulky a kliknutím na dva sloupce definuj vztah. Model pouze popisuje vazby; neprovádí SQL JOIN."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        var toolbar = new WrapPanel();
        top.Children.Add(toolbar);
        toolbar.Children.Add(_modelChoice);
        toolbar.Children.Add(MakeButton(T("SYS15.Graph.New", "Nový model"), (_, _) => NewModel()));
        toolbar.Children.Add(_name);
        toolbar.Children.Add(MakeButton(T("SYS15.Graph.Save", "Uložit model"), (_, _) => SaveModel()));
        toolbar.Children.Add(MakeButton(T("SYS15.Graph.Delete", "Smazat model"), (_, _) => DeleteModel()));
        toolbar.Children.Add(MakeButton(T("SYS15.Graph.Reload", "Obnovit"), (_, _) => ReloadModels(true)));
        _modelChoice.Margin = new Thickness(0, 0, 8, 7);
        _name.Margin = new Thickness(0, 0, 8, 7);
        _modelChoice.SelectionChanged += (_, _) =>
        {
            if (!_switching && _modelChoice.SelectedItem is DmsGraphModelDefinition model)
            {
                if (_dirty && MessageBox.Show(
                    T("SYS15.Graph.Discard", "Zahodit neuložené změny modelu?"), "SYS15",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    RefreshModelChoice();
                    return;
                }
                OpenModel(model);
            }
        };
        _name.TextChanged += (_, _) => MarkDirty();
        top.Children.Add(_status);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(315) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        layout.Children.Add(main);

        var left = new StackPanel { Margin = new Thickness(0, 4, 12, 0) };
        main.Children.Add(left);
        left.Children.Add(Heading(T("SYS15.Graph.Sources", "Datové zdroje")));
        left.Children.Add(_sourceChoice);
        left.Children.Add(_sourceDetail);
        var sourceActions = new WrapPanel();
        left.Children.Add(sourceActions);
        sourceActions.Children.Add(MakeButton(T("SYS15.Graph.LoadSchema", "Načíst tabulky"), async (_, _) => await LoadSchemaAsync()));
        sourceActions.Children.Add(MakeButton(T("SYS15.Graph.RefreshSources", "Obnovit zdroje"), (_, _) => LoadSources()));
        left.Children.Add(Heading(T("SYS15.Graph.Tables", "Tabulky / listy")));
        left.Children.Add(_tableChoice);
        left.Children.Add(MakeButton(T("SYS15.Graph.AddNode", "Přidat do modelu"), (_, _) => AddNode()));
        left.Children.Add(new TextBlock
        {
            Text = T("SYS15.Graph.Limits", "MES načítá pouze strukturu SQL. XLSX / CSV / JSON načítají názvy sloupců. Zdrojová data ani hesla se do modelu neukládají."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Opacity = .8
        });
        _sourceChoice.Margin = new Thickness(0, 4, 0, 8);
        _tableChoice.Margin = new Thickness(0, 4, 0, 8);
        _sourceChoice.SelectionChanged += (_, _) =>
        {
            _tableChoice.ItemsSource = null;
            _sourceDetail.Text = string.Empty;
            if (_sourceChoice.SelectedItem is DmsDataSourceEntry src)
            {
                _sourceDetail.Text = $"{src.Kind}  ·  {src.Location}";
                if (_tableCache.TryGetValue(src.Code, out var cached))
                {
                    _tableChoice.ItemsSource = cached;
                    _tableChoice.SelectedIndex = cached.Count == 0 ? -1 : 0;
                }
            }
        };

        BuildGraphViewport(main);
        _canvas.SetResourceReference(Panel.BackgroundProperty, "DmsBackgroundBrush");

        var right = new DockPanel { Margin = new Thickness(12, 4, 0, 0) };
        Grid.SetColumn(right, 2);
        main.Children.Add(right);
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        right.Children.Add(heading);
        heading.Children.Add(Heading(T("SYS15.Graph.Relations", "Vztahy mezi tabulkami")));
        heading.Children.Add(_selection);
        heading.Children.Add(MakeButton(T("SYS15.Graph.Preview", "Zobrazit data tabulky"), (_, _) => PreviewSelectedNode()));
        heading.Children.Add(MakeButton(T("SYS15.Graph.RemoveNode", "Odebrat vybranou tabulku"), (_, _) => RemoveNode()));
        heading.Children.Add(MakeButton(T("SYS15.Graph.RemoveRelation", "Odebrat vybranou vazbu"), (_, _) => RemoveRelation()));
        right.Children.Add(_relationships);
    }

    // The model canvas is measured at its *scaled* size by ScrollViewer (LayoutTransform).
    // The separate horizontal bar has a comfortable 23px hit target and a proportional thumb.
    private void BuildGraphViewport(Grid main)
    {
        var center = new DockPanel();
        Grid.SetColumn(center, 1);
        main.Children.Add(center);

        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
        DockPanel.SetDock(controls, Dock.Top);
        center.Children.Add(controls);
        controls.Children.Add(MakeButton(T("SYS15.Graph.ZoomOut", "−"), (_, _) => SetZoom(_zoom - .10)));
        controls.Children.Add(_zoomControl);
        controls.Children.Add(_zoomText);
        controls.Children.Add(MakeButton(T("SYS15.Graph.ZoomIn", "+"), (_, _) => SetZoom(_zoom + .10)));
        controls.Children.Add(MakeButton(T("SYS15.Graph.ZoomReset", "100 %"), (_, _) => SetZoom(1)));
        controls.Children.Add(MakeButton(T("SYS15.Graph.ZoomFit", "Přizpůsobit"), (_, _) => FitGraph()));
        _zoomControl.ValueChanged += (_, e) => SetZoom(e.NewValue);
        _zoomText.Text = "100 %";

        // Hide the tiny built-in horizontal bar, replacing it with a full-height one.
        DockPanel.SetDock(_horizontalPan, Dock.Bottom);
        center.Children.Add(_horizontalPan);
        _horizontalPan.ValueChanged += (_, e) =>
        {
            if (!_updatingPan) _graphScroll?.ScrollToHorizontalOffset(e.NewValue);
        };

        _canvas.LayoutTransform = _scale;
        _graphScroll = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            CanContentScroll = false,
            PanningMode = PanningMode.None
        };
        _graphScroll.ScrollChanged += (_, _) => UpdatePanBar();
        _graphScroll.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SetZoom(_zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), e.GetPosition(_graphScroll));
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _graphScroll.ScrollToHorizontalOffset(_graphScroll.HorizontalOffset - e.Delta * .8);
            }
            else
            {
                _graphScroll.ScrollToVerticalOffset(_graphScroll.VerticalOffset - e.Delta * .8);
            }
            e.Handled = true;
        };
        _graphScroll.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _panStart = e.GetPosition(_graphScroll);
            _panStartX = _graphScroll.HorizontalOffset;
            _panStartY = _graphScroll.VerticalOffset;
            _graphScroll.CaptureMouse();
            e.Handled = true;
        };
        _graphScroll.PreviewMouseMove += (_, e) =>
        {
            if (_panStart is not Point start || e.MiddleButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(_graphScroll);
            _graphScroll.ScrollToHorizontalOffset(_panStartX + start.X - position.X);
            _graphScroll.ScrollToVerticalOffset(_panStartY + start.Y - position.Y);
            e.Handled = true;
        };
        _graphScroll.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _panStart = null;
            if (_graphScroll.IsMouseCaptured) _graphScroll.ReleaseMouseCapture();
            e.Handled = true;
        };
        _graphScroll.LostMouseCapture += (_, _) => _panStart = null;
        center.Children.Add(_graphScroll);
    }

    private void UpdatePanBar()
    {
        if (_graphScroll is null) return;
        _updatingPan = true;
        try
        {
            _horizontalPan.Maximum = Math.Max(0, _graphScroll.ExtentWidth - _graphScroll.ViewportWidth);
            _horizontalPan.ViewportSize = Math.Max(1, _graphScroll.ViewportWidth);
            _horizontalPan.LargeChange = Math.Max(60, _graphScroll.ViewportWidth * .8);
            _horizontalPan.Value = Math.Clamp(_graphScroll.HorizontalOffset, 0, _horizontalPan.Maximum);
            _horizontalPan.IsEnabled = _horizontalPan.Maximum > 0;
        }
        finally { _updatingPan = false; }
    }

    private void SetZoom(double requested, Point? cursor = null)
    {
        var next = Math.Clamp(requested, .35, 2.25);
        if (Math.Abs(next - _zoom) < .001) return;
        var scroll = _graphScroll;
        var point = cursor ?? (scroll is null
            ? new Point(0, 0)
            : new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2));
        var worldX = scroll is null ? 0 : (scroll.HorizontalOffset + point.X) / _zoom;
        var worldY = scroll is null ? 0 : (scroll.VerticalOffset + point.Y) / _zoom;
        _zoom = next;
        _scale.ScaleX = next;
        _scale.ScaleY = next;
        if (Math.Abs(_zoomControl.Value - next) > .001) _zoomControl.Value = next;
        _zoomText.Text = $"{Math.Round(next * 100):0} %";
        if (scroll is not null)
        {
            scroll.UpdateLayout();
            scroll.ScrollToHorizontalOffset(worldX * next - point.X);
            scroll.ScrollToVerticalOffset(worldY * next - point.Y);
            UpdatePanBar();
        }
    }

    private void FitGraph()
    {
        if (_graphScroll is null) return;
        var width = Math.Max(500, _current.Nodes.Count == 0 ? 850 : _current.Nodes.Max(n => n.X + 300));
        var height = Math.Max(300, _current.Nodes.Count == 0 ? 550 : _current.Nodes.Max(n => n.Y + 45 + n.Columns.Count * 28));
        var fit = Math.Min((_graphScroll.ViewportWidth - 30) / width,
            (_graphScroll.ViewportHeight - 30) / height);
        SetZoom(Math.Clamp(fit, .35, 2.25), new Point(0, 0));
        _graphScroll.ScrollToHorizontalOffset(0);
        _graphScroll.ScrollToVerticalOffset(0);
    }

    private void ResizeCanvasForNodes()
    {
        _canvas.Width = Math.Max(1250, _current.Nodes.Count == 0 ? 0 : _current.Nodes.Max(n => n.X + 360));
        _canvas.Height = Math.Max(900, _current.Nodes.Count == 0 ? 0 : _current.Nodes.Max(n => n.Y + 100 + n.Columns.Count * 28));
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0, 8, 0, 5),
        TextWrapping = TextWrapping.Wrap
    };

    private void SetStatus(string message) => _status.Text = message;
    private void MarkDirty()
    {
        if (_switching) return;
        _dirty = true;
        SetStatus(T("SYS15.Graph.Unsaved", "Neuložené změny modelu"));
    }

    private void RefreshModelChoice()
    {
        _switching = true;
        _modelChoice.ItemsSource = null;
        _modelChoice.ItemsSource = _saved;
        _modelChoice.SelectedItem = _saved.FirstOrDefault(x => x.Code == _current.Code);
        _switching = false;
    }

    private void ReloadModels(bool confirm)
    {
        if (confirm && _dirty && MessageBox.Show(
            T("SYS15.Graph.Discard", "Zahodit neuložené změny modelu?"), "SYS15",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var read = _catalog.Load();
            _saved = read.Models;
            _fingerprint = read.Fingerprint;
            if (_saved.Count > 0) OpenModel(_saved[0]);
            else NewModel(false);
            RefreshModelChoice();
            SetStatus(T("SYS15.Graph.Loaded", "Modely načteny ze sdílené konfigurace."));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            _logger.Error("SYS15 graph catalog load failed", ex);
        }
    }

    private void OpenModel(DmsGraphModelDefinition model)
    {
        _current = DmsGraphModelCatalog.Clone(model);
        _switching = true;
        _name.Text = _current.Name;
        _switching = false;
        _dirty = false;
        _pendingField = null;
        _selectedNodeId = null;
        RefreshModelChoice();
        RenderGraph();
    }

    private void NewModel(bool dirty = true)
    {
        if (dirty && _dirty && MessageBox.Show(
            T("SYS15.Graph.Discard", "Zahodit neuložené změny modelu?"), "SYS15",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var index = 1;
        while (_saved.Any(x => x.Code.Equals($"MODEL_{index:000}", StringComparison.OrdinalIgnoreCase))) index++;
        _current = new DmsGraphModelDefinition { Code = $"MODEL_{index:000}", Name = T("SYS15.Graph.DefaultName", "Nový model") };
        _switching = true;
        _modelChoice.SelectedIndex = -1;
        _name.Text = _current.Name;
        _switching = false;
        _dirty = dirty;
        _selectedNodeId = null;
        _pendingField = null;
        RenderGraph();
        if (dirty) MarkDirty();
    }

    private void LoadSources()
    {
        try
        {
            var selectedCode = (_sourceChoice.SelectedItem as DmsDataSourceEntry)?.Code;
            var enabled = _sources.Load().Where(x => x.Enabled).ToList();
            // Replacing an XLSX path or source kind must also invalidate its old columns.
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in enabled)
            {
                keys.Add(source.Code);
                var cacheKey = source.Kind + "|" + source.Location;
                if (_sourceCacheKeys.TryGetValue(source.Code, out var previous) &&
                    !string.Equals(previous, cacheKey, StringComparison.OrdinalIgnoreCase))
                    _tableCache.Remove(source.Code);
                _sourceCacheKeys[source.Code] = cacheKey;
            }
            foreach (var stale in _sourceCacheKeys.Keys.Where(k => !keys.Contains(k)).ToArray())
            {
                _sourceCacheKeys.Remove(stale);
                _tableCache.Remove(stale);
            }
            _sourceChoice.ItemsSource = enabled;
            _sourceChoice.SelectedItem = enabled.FirstOrDefault(x =>
                string.Equals(x.Code, selectedCode, StringComparison.OrdinalIgnoreCase))
                ?? enabled.FirstOrDefault();
            if (enabled.Count == 0)
                SetStatus(T("SYS15.Graph.NoSources", "Nejsou k dispozici žádné aktivní datové zdroje."));
        }
        catch (Exception ex) { SetStatus(ex.Message); _logger.Error("SYS15 graph source catalog load failed", ex); }
    }

    private async Task LoadSchemaAsync()
    {
        if (_sourceChoice.SelectedItem is not DmsDataSourceEntry entry)
        {
            SetStatus(T("SYS15.Graph.PickSource", "Nejdříve vyber datový zdroj."));
            return;
        }
        SetStatus(T("SYS15.Graph.Reading", "Načítám strukturu zdroje…"));
        try
        {
            List<SchemaTable> tables;
            if (entry.Kind == "MES_SQL") tables = await ReadMesSchemaAsync();
            else if (entry.Kind is "XLSX" or "CSV" or "JSON") tables = await Task.Run(() => ReadFileSchema(entry));
            else throw new InvalidOperationException(T("SYS15.Graph.Unsupported", "Pro tento zdroj zatím není dostupný prohlížeč struktury."));
            if (_sourceChoice.SelectedItem is not DmsDataSourceEntry actual || actual.Code != entry.Code) return;
            _tableCache[entry.Code] = tables;
            _tableChoice.ItemsSource = tables;
            _tableChoice.SelectedIndex = tables.Count > 0 ? 0 : -1;
            SetStatus($"{T("SYS15.Graph.Found", "Načtených tabulek / listů")}: {tables.Count}");
            _logger.AdminAction("SYS15", "GRAPH_SCHEMA_INSPECT", _user, $"Source={entry.Code}; Tables={tables.Count}");
        }
        catch (Exception ex)
        {
            SetStatus($"{entry.Name}: {ex.Message}");
            _logger.Error($"SYS15 graph schema inspection failed. Source={entry.Code}; Kind={entry.Kind}", ex);
        }
    }

    private async Task<List<SchemaTable>> ReadMesSchemaAsync()
    {
        var settingsNames = new[] { "mes-database-settings.json", "mes-reporting-settings.json", "mes-sql-settings.json", "mes-database.json", "mes-reporting.json" };
        var path = settingsNames.Select(n => Path.Combine(_configRoot, n)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("MESSET configuration was not found.");
        var settings = new MesDatabaseSettingsService().Load(path);
        if (!settings.IsEnabled) throw new InvalidOperationException("MESSET is disabled.");
        await using var connection = await new MesSqlConnectionFactory().OpenAsync(settings);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = Math.Min(25, settings.CommandTimeoutSeconds);
        cmd.CommandText = """
            SELECT TOP (5000) sch.name, obj.name, col.name, typ.name
            FROM sys.objects AS obj
            JOIN sys.schemas AS sch ON sch.schema_id = obj.schema_id
            JOIN sys.columns AS col ON col.object_id = obj.object_id
            JOIN sys.types AS typ ON typ.user_type_id = col.user_type_id
            WHERE obj.type IN ('U','V') AND obj.is_ms_shipped = 0 AND sch.name = @schema
            ORDER BY sch.name, obj.name, col.column_id
            """;
        cmd.Parameters.AddWithValue("@schema", settings.ReportingSchema);
        var lookup = new Dictionary<string, SchemaTable>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var full = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (!lookup.TryGetValue(full, out var table))
            {
                table = new SchemaTable { Name = full };
                lookup.Add(full, table);
            }
            if (table.Columns.Count < 150)
                table.Columns.Add(new DmsGraphColumn { Name = reader.GetString(2), DataType = reader.GetString(3) });
        }
        return lookup.Values.Where(t => t.Columns.Count > 0).OrderBy(t => t.Name).ToList();
    }

    private static List<SchemaTable> ReadFileSchema(DmsDataSourceEntry src)
    {
        var limit = src.Kind == "XLSX" ? 50_000_000L : 20_000_000L;
        var file = new FileInfo(src.Location);
        if (!file.Exists || file.Length > limit) throw new InvalidOperationException("File is missing or exceeds inspection limit.");
        var result = new List<SchemaTable>();
        if (src.Kind == "XLSX")
        {
            // Read the workbook through a shared, read-only stream. SYS15 is often
            // used while the same XLSX file is open in Excel on a network drive.
            using var input = new FileStream(src.Location, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(input);
            foreach (var sheet in workbook.Worksheets.Take(100))
            {
                var used = sheet.FirstRowUsed();
                var headers = used?.CellsUsed().Take(150).Select((c, i) => string.IsNullOrWhiteSpace(c.GetString()) ? $"Column_{i + 1}" : c.GetString().Trim()).ToArray()
                    ?? Array.Empty<string>();
                AddSchema(result, sheet.Name, headers);
            }
        }
        else if (src.Kind == "CSV")
        {
            var header = File.ReadLines(src.Location).FirstOrDefault() ?? "";
            var separator = header.Count(c => c == ';') >= header.Count(c => c == ',') ? ';' : ',';
            AddSchema(result, Path.GetFileName(src.Location), header.TrimStart('\uFEFF').Split(separator).Take(150));
        }
        else
        {
            using var stream = File.OpenRead(src.Location);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
            if (root.ValueKind == JsonValueKind.Object)
                AddSchema(result, Path.GetFileName(src.Location), root.EnumerateObject().Take(150).Select(p => p.Name));
        }
        return result;
    }

    private static void AddSchema(List<SchemaTable> tables, string name, IEnumerable<string> columns)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = columns.Select((s, i) => string.IsNullOrWhiteSpace(s) ? $"Column_{i+1}" : s.Trim())
            .Where(s => unique.Add(s)).Take(150).Select(s => new DmsGraphColumn { Name = s, DataType = "Text" }).ToList();
        if (items.Count > 0 && !tables.Any(t => t.Name == name)) tables.Add(new SchemaTable { Name = name, Columns = items });
    }

    private void AddNode()
    {
        if (_sourceChoice.SelectedItem is not DmsDataSourceEntry source || _tableChoice.SelectedItem is not SchemaTable table) return;
        if (_current.Nodes.Any(n => n.SourceCode == source.Code && n.ObjectName == table.Name))
        { SetStatus(T("SYS15.Graph.AlreadyAdded", "Tabulka už je v modelu.")); return; }
        var i = _current.Nodes.Count;
        var node = new DmsGraphNode
        {
            SourceCode = source.Code,
            ObjectName = table.Name,
            X = 55 + (i % 5) * 310,
            Y = 60 + (i / 5) * 375,
            Columns = table.Columns.Select(c => new DmsGraphColumn { Name = c.Name, DataType = c.DataType }).ToList()
        };
        _current.Nodes.Add(node);
        _selectedNodeId = node.Id;
        MarkDirty();
        RenderGraph();
    }

    private void RemoveNode()
    {
        var node = _current.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
        if (node is null) return;
        if (MessageBox.Show(T("SYS15.Graph.RemoveNodeConfirm", "Odebrat tabulku i její vztahy?"), "SYS15",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _current.Nodes.Remove(node);
        _current.Relationships.RemoveAll(r => r.FromNodeId == node.Id || r.ToNodeId == node.Id);
        _selectedNodeId = null;
        _pendingField = null;
        MarkDirty();
        RenderGraph();
    }

    private void RemoveRelation()
    {
        if (_relationships.SelectedItem is not RelationChoice choice) return;
        _current.Relationships.RemoveAll(x => x.Id == choice.Relationship.Id);
        MarkDirty();
        RenderGraph();
    }

    private void RenderGraph()
    {
        ResizeCanvasForNodes();
        _canvas.Children.Clear();
        _cards.Clear();
        _edgeElements.Clear();
        _pendingField = null;
        foreach (var node in _current.Nodes)
        {
            var card = BuildCard(node);
            _cards[node.Id] = card;
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
            _canvas.Children.Add(card);
        }
        DrawEdges();
        RefreshRelationships();
        UpdateSelection();
    }

    private Border BuildCard(DmsGraphNode node)
    {
        var card = new Border
        {
            Width = 255, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.SlateGray, Padding = new Thickness(1), Tag = node.Id,
            Effect = null
        };
        card.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        var stack = new StackPanel();
        card.Child = stack;
        card.PreviewMouseRightButtonDown += (_, _) => { _selectedNodeId = node.Id; UpdateSelection(); };
        card.ContextMenu = BuildNodeContextMenu(node);
        var title = new Border { Background = new SolidColorBrush(Color.FromRgb(23, 100, 117)), Padding = new Thickness(8, 7, 8, 7), Cursor = Cursors.SizeAll };
        title.Child = new TextBlock { Text = node.ObjectName + "  [" + node.SourceCode + "]", Foreground = Brushes.White,
            FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = node.ObjectName };
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ShowTablePreview(node); e.Handled = true; return; }
            _selectedNodeId = node.Id;
            _dragNode = node;
            _dragHandle = title;
            _lastMouse = e.GetPosition(_canvas);
            title.CaptureMouse();
            UpdateSelection();
            e.Handled = true;
        };
        title.MouseMove += (_, e) =>
        {
            if (_dragNode is null || !ReferenceEquals(_dragHandle, title) || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(_canvas);
            var dx = pos.X - _lastMouse.X;
            var dy = pos.Y - _lastMouse.Y;
            if (Math.Abs(dx) + Math.Abs(dy) < .3) return;
            node.X = Math.Clamp(node.X + dx, 0, 7500);
            node.Y = Math.Clamp(node.Y + dy, 0, 7500);
            ResizeCanvasForNodes();
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
            _lastMouse = pos;
            MarkDirty();
            DrawEdges();
        };
        title.MouseLeftButtonUp += (_, e) => { if (ReferenceEquals(_dragHandle, title)) EndDrag(); e.Handled = true; };
        title.LostMouseCapture += (_, _) => { if (ReferenceEquals(_dragHandle, title)) EndDrag(); };
        stack.Children.Add(title);
        foreach (var field in node.Columns)
        {
            var button = new Button
            {
                Content = field.Name + (string.IsNullOrWhiteSpace(field.DataType) ? "" : $"  · {field.DataType}"),
                ToolTip = T("SYS15.Graph.ClickField", "Klikni na dva sloupce pro vytvoření vazby"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(0, 0, 0, .5),
                BorderBrush = Brushes.LightSlateGray,
                Background = Brushes.Transparent,
                Padding = new Thickness(8, 4, 4, 4),
                Height = 28, FontSize = 11.5
            };
            button.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
            button.Click += (_, _) => SelectField(node, field, button);
            var fieldMenu = StyledMenu();
            fieldMenu.Items.Add(AddMenuAction(T("SYS15.Graph.RelateField", "Vybrat pro vazbu"), (_, _) => SelectField(node, field, button)));
            fieldMenu.Items.Add(AddMenuAction(T("SYS15.Graph.CopyColumn", "Kopírovat název sloupce"), (_, _) => Clipboard.SetText(field.Name)));
            button.ContextMenu = fieldMenu;
            stack.Children.Add(button);
        }
        return card;
    }

    private static ContextMenu StyledMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.BackgroundProperty, "DmsPanelBrush");
        menu.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        return menu;
    }

    private static MenuItem AddMenuAction(string label, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = label };
        item.Click += handler;
        return item;
    }

    private ContextMenu BuildNodeContextMenu(DmsGraphNode node)
    {
        var menu = StyledMenu();
        menu.Items.Add(AddMenuAction(T("SYS15.Graph.Preview", "Zobrazit data tabulky"), (_, _) => ShowTablePreview(node)));
        menu.Items.Add(AddMenuAction(T("SYS15.Graph.CopyTable", "Kopírovat název tabulky"), (_, _) => Clipboard.SetText(node.ObjectName)));
        menu.Items.Add(new Separator());
        menu.Items.Add(AddMenuAction(T("SYS15.Graph.RelationMenu", "Vybrat vztahy tabulky"), (_, _) => SelectNodeRelationships(node)));
        menu.Items.Add(AddMenuAction(T("SYS15.Graph.RemoveNode", "Odebrat vybranou tabulku"), (_, _) =>
        {
            _selectedNodeId = node.Id;
            RemoveNode();
        }));
        return menu;
    }

    private void SelectNodeRelationships(DmsGraphNode node)
    {
        _selectedNodeId = node.Id;
        UpdateSelection();
        var first = _current.Relationships.FirstOrDefault(r => r.FromNodeId == node.Id || r.ToNodeId == node.Id);
        if (first is null)
        {
            SetStatus(T("SYS15.Graph.NoRelations", "Tabulka zatím nemá žádnou vazbu."));
            return;
        }
        _relationships.SelectedItem = _relationships.Items.OfType<RelationChoice>()
            .FirstOrDefault(x => x.Relationship.Id == first.Id);
        _relationships.Focus();
    }

    private void PreviewSelectedNode()
    {
        var node = _current.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
        if (node is not null) ShowTablePreview(node);
        else SetStatus(T("SYS15.Graph.SelectTable", "Nejdříve vyber tabulku."));
    }

    // Preview never stores row values in DmsGraphModelDefinition and never writes to the source.
    private void ShowTablePreview(DmsGraphNode node)
    {
        var source = _sources.Load().FirstOrDefault(s => s.Code.Equals(node.SourceCode, StringComparison.OrdinalIgnoreCase));
        if (source is null || !source.Enabled)
        {
            SetStatus(T("SYS15.Graph.SourceUnavailable", "Datový zdroj není dostupný nebo je vypnutý."));
            return;
        }
        var window = new Window
        {
            Title = $"{T("SYS15.Graph.Preview", "Zobrazit data tabulky")} – {node.ObjectName}",
            Width = 1000, Height = 620, MinWidth = 650, MinHeight = 380,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.SetResourceReference(Control.BackgroundProperty, "DmsPanelBrush");
        window.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        var content = new DockPanel { Margin = new Thickness(14) };
        window.Content = content;
        var status = new TextBlock
        {
            Text = T("SYS15.Graph.PreviewLoading", "Načítám nejvýše 100 řádků (pouze pro čtení)…"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(status, Dock.Top);
        content.Children.Add(status);
        var grid = new DataGrid
        {
            AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended,
            EnableRowVirtualization = true, EnableColumnVirtualization = true
        };
        grid.SetResourceReference(Control.BackgroundProperty, "DmsPanelBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        content.Children.Add(grid);
        var cts = new CancellationTokenSource();
        window.Closed += (_, _) => { cts.Cancel(); };
        window.Loaded += async (_, _) =>
        {
            DmsWindowChromeStyler.ApplyFromResources(window);
            try
            {
                var table = source.Kind == "MES_SQL"
                    ? await LoadMesPreviewAsync(node, cts.Token)
                    : await Task.Run(() => LoadFilePreview(source, node, cts.Token), cts.Token);
                if (cts.IsCancellationRequested) return;
                grid.ItemsSource = table.DefaultView;
                status.Text = $"{T("SYS15.Graph.PreviewRows", "Načtené řádky")}: {table.Rows.Count} / 100  ·  {T("SYS15.Graph.ReadOnly", "Pouze pro čtení")}";
                _logger.AdminAction("SYS15", "GRAPH_TABLE_PREVIEW", _user,
                    $"Source={source.Code}; Table={node.ObjectName}; Rows={table.Rows.Count}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested)
                {
                    status.Text = ex.Message;
                    _logger.Error($"SYS15 preview failed: {source.Code}.{node.ObjectName}", ex);
                }
            }
        };
        window.Show();
    }

    private async Task<DataTable> LoadMesPreviewAsync(DmsGraphNode node, CancellationToken token)
    {
        var settingsNames = new[] { "mes-database-settings.json", "mes-reporting-settings.json", "mes-sql-settings.json", "mes-database.json", "mes-reporting.json" };
        var path = settingsNames.Select(n => Path.Combine(_configRoot, n)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("MESSET configuration was not found.");
        var settings = new MesDatabaseSettingsService().Load(path);
        if (!settings.IsEnabled) throw new InvalidOperationException("MESSET is disabled.");
        var parts = node.ObjectName.Split('.');
        if (parts.Length != 2 || !parts[0].Equals(settings.ReportingSchema, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("SYS15.Graph.SchemaDenied", "Tabulka není ve schváleném analytickém schématu MES."));
        var columns = node.Columns.Take(80).ToList();
        if (columns.Count == 0) throw new InvalidOperationException("No columns available.");
        // Escaping the closing bracket makes every column/table name a single SQL identifier.
        static string Q(string s) => "[" + s.Replace("]", "]]", StringComparison.Ordinal) + "]";
        await using var connection = await new MesSqlConnectionFactory().OpenAsync(settings, token);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = Math.Min(15, settings.CommandTimeoutSeconds);
        command.CommandText = $"SELECT TOP (@rows) {string.Join(", ", columns.Select(c => Q(c.Name)))} FROM {Q(parts[0])}.{Q(parts[1])}";
        command.Parameters.AddWithValue("@rows", 100);
        await using var reader = await command.ExecuteReaderAsync(token);
        var data = CreatePreviewTable(columns.Select(c => c.Name));
        while (data.Rows.Count < 100 && await reader.ReadAsync(token))
        {
            var row = data.NewRow();
            for (var i = 0; i < data.Columns.Count; i++)
                row[i] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.CurrentCulture) ?? "";
            data.Rows.Add(row);
        }
        return data;
    }

    private static DataTable LoadFilePreview(DmsDataSourceEntry source, DmsGraphNode node, CancellationToken token)
    {
        var file = new FileInfo(source.Location);
        var maxBytes = source.Kind == "XLSX" ? 50_000_000L : 20_000_000L;
        if (!file.Exists || file.Length > maxBytes)
            throw new InvalidOperationException("Source file is missing or exceeds the preview size limit.");
        var columns = node.Columns.Take(80).Select(c => c.Name).ToList();
        var data = CreatePreviewTable(columns);
        if (source.Kind == "XLSX")
        {
            using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(input);
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(node.ObjectName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Worksheet not found in the source file.");
            var header = sheet.FirstRowUsed();
            if (header is null) return data;
            var map = header.CellsUsed().Select((cell, i) => new { Name = string.IsNullOrWhiteSpace(cell.GetString()) ? $"Column_{i+1}" : cell.GetString().Trim(), Index = cell.Address.ColumnNumber })
                .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);
            foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber()).Take(100))
            {
                token.ThrowIfCancellationRequested();
                var values = columns.Select(name => map.TryGetValue(name, out var idx) ? row.Cell(idx).GetFormattedString() : "");
                AddPreviewRow(data, values);
            }
        }
        else if (source.Kind == "CSV")
        {
            using var reader = new StreamReader(file.FullName, Encoding.UTF8, true);
            var header = reader.ReadLine() ?? string.Empty;
            var delimiter = header.Count(c => c == ';') >= header.Count(c => c == ',') ? ';' : ',';
            for (var i = 0; i < 100 && !reader.EndOfStream; i++)
            {
                token.ThrowIfCancellationRequested();
                AddPreviewRow(data, SplitCsvRow(reader.ReadLine() ?? string.Empty, delimiter));
            }
        }
        else if (source.Kind == "JSON")
        {
            using var input = File.OpenRead(file.FullName);
            using var json = JsonDocument.Parse(input);
            var root = json.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().Take(100)
                : new[] { root }.AsEnumerable();
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.ValueKind != JsonValueKind.Object) continue;
                AddPreviewRow(data, columns.Select(name =>
                    item.TryGetProperty(name, out var property) ? property.ToString() : ""));
            }
        }
        else throw new InvalidOperationException("Preview is supported for MES, XLSX, CSV and JSON sources.");
        return data;
    }

    private static DataTable CreatePreviewTable(IEnumerable<string> columnNames)
    {
        var table = new DataTable();
        foreach (var name in columnNames)
            table.Columns.Add(name, typeof(string));
        return table;
    }

    private static void AddPreviewRow(DataTable table, IEnumerable<string> values)
    {
        var row = table.NewRow();
        foreach (var (value, i) in values.Take(table.Columns.Count).Select((v, i) => (v, i)))
            row[i] = value;
        table.Rows.Add(row);
    }

    private static List<string> SplitCsvRow(string line, char delimiter)
    {
        var values = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == delimiter && !quoted) { values.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        values.Add(field.ToString());
        return values;
    }

    private void EndDrag()
    {
        var handle = _dragHandle;
        _dragHandle = null;
        _dragNode = null;
        if (handle is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
    }

    private void SelectField(DmsGraphNode node, DmsGraphColumn col, Button button)
    {
        _selectedNodeId = node.Id;
        UpdateSelection();
        if (_pendingField is null)
        {
            _pendingField = (node.Id, col.Name);
            button.SetResourceReference(Control.BackgroundProperty, "DmsAccentBrush");
            button.SetResourceReference(Control.ForegroundProperty, "DmsOnAccentBrush");
            SetStatus($"{T("SYS15.Graph.FirstField", "První sloupec")}: {node.ObjectName}.{col.Name} → {T("SYS15.Graph.SecondField", "vyber druhý sloupec")}");
            return;
        }
        var first = _pendingField.Value;
        _pendingField = null;
        if (first.NodeId == node.Id)
        { SetStatus(T("SYS15.Graph.DifferentNode", "Vazba musí spojovat dvě různé tabulky.")); RenderGraph(); return; }
        var from = _current.Nodes.FirstOrDefault(n => n.Id == first.NodeId);
        if (from is null) { RenderGraph(); return; }
        var leftType = from.Columns.First(c => c.Name == first.Column).DataType;
        var rightType = col.DataType;
        var card = CreateRelationshipDialog($"{from.ObjectName}.{first.Column}", $"{node.ObjectName}.{col.Name}", leftType, rightType);
        if (card is null) { RenderGraph(); return; }
        var edge = new DmsGraphRelationship
        {
            FromNodeId = from.Id, FromColumn = first.Column,
            ToNodeId = node.Id, ToColumn = col.Name,
            Cardinality = card.Value.Cardinality, FilterDirection = card.Value.Direction
        };
        _current.Relationships.Add(edge);
        try { DmsGraphModelCatalog.Validate(new[] { _current }); }
        catch (Exception ex)
        {
            _current.Relationships.Remove(edge);
            SetStatus(ex.Message);
            RenderGraph();
            return;
        }
        MarkDirty();
        RenderGraph();
    }

    private (string Cardinality, string Direction)? CreateRelationshipDialog(string from, string to, string leftType, string rightType)
    {
        var window = new Window
        {
            Title = T("SYS15.Graph.NewRelation", "Nová vazba tabulek"), Width = 540, Height = 355,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        // Standalone WPF windows do not automatically inherit the DMS workspace
        // background. Without these resources the white default window makes
        // theme-colored (white) text unreadable in Dark / HG mode.
        window.SetResourceReference(Control.BackgroundProperty, "DmsPanelBrush");
        window.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        window.Loaded += (_, _) => DmsWindowChromeStyler.ApplyFromResources(window);
        var panel = new StackPanel { Margin = new Thickness(20) };
        window.Content = panel;
        panel.Children.Add(new TextBlock { Text = from + " → " + to, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (!string.IsNullOrWhiteSpace(leftType) && !string.IsNullOrWhiteSpace(rightType) &&
            !leftType.Equals(rightType, StringComparison.OrdinalIgnoreCase))
            panel.Children.Add(new TextBlock { Text = $"{T("SYS15.Graph.TypeWarning", "Pozor: rozdílné datové typy")}: {leftType} ↔ {rightType}",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange });
        panel.Children.Add(Heading(T("SYS15.Graph.Cardinality", "Kardinalita")));
        var cardinalities = new[]
        {
            new Option("OneToOne", "1 : 1"), new Option("OneToMany", "1 : N"),
            new Option("ManyToOne", "N : 1"), new Option("ManyToMany", "N : N")
        };
        var cardinality = new ComboBox { ItemsSource = cardinalities, SelectedIndex = 1, MinHeight = 32 };
        cardinality.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");
        cardinality.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        panel.Children.Add(cardinality);
        panel.Children.Add(Heading(T("SYS15.Graph.Direction", "Směr filtrování")));
        var directions = new[]
        {
            new Option("Forward", T("SYS15.Graph.Forward", "Z první do druhé")),
            new Option("Reverse", T("SYS15.Graph.Reverse", "Z druhé do první")),
            new Option("Both", T("SYS15.Graph.Both", "Obousměrně"))
        };
        var direction = new ComboBox { ItemsSource = directions, SelectedIndex = 0, MinHeight = 32 };
        direction.SetResourceReference(Control.BackgroundProperty, "DmsBackgroundBrush");
        direction.SetResourceReference(Control.ForegroundProperty, "DmsForegroundBrush");
        panel.Children.Add(direction);
        var buttons = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
        buttons.Children.Add(MakeButton(T("SYS15.Graph.Confirm", "Vytvořit vazbu"), (_, _) => { window.DialogResult = true; window.Close(); }));
        buttons.Children.Add(MakeButton(T("SYS15.Graph.Cancel", "Zrušit"), (_, _) => { window.DialogResult = false; window.Close(); }));
        panel.Children.Add(buttons);
        return window.ShowDialog() == true
            ? (((Option)cardinality.SelectedItem).Value, ((Option)direction.SelectedItem).Value)
            : null;
    }

    private void UpdateSelection()
    {
        var selected = _current.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
        _selection.Text = selected is null
            ? T("SYS15.Graph.DragHint", "Přesuň tabulku za její záhlaví. Vyber dva sloupce pro vztah.")
            : $"{T("SYS15.Graph.Selected", "Vybraná tabulka")}: {selected.ObjectName}";
        foreach (var item in _cards)
            item.Value.BorderBrush = item.Key == _selectedNodeId ? Brushes.DeepSkyBlue : Brushes.SlateGray;
    }

    private void DrawEdges()
    {
        foreach (var visual in _edgeElements) _canvas.Children.Remove(visual);
        _edgeElements.Clear();
        foreach (var rel in _current.Relationships)
        {
            var a = _current.Nodes.FirstOrDefault(n => n.Id == rel.FromNodeId);
            var b = _current.Nodes.FirstOrDefault(n => n.Id == rel.ToNodeId);
            if (a is null || b is null) continue;
            var indexA = a.Columns.FindIndex(c => c.Name == rel.FromColumn);
            var indexB = b.Columns.FindIndex(c => c.Name == rel.ToColumn);
            if (indexA < 0 || indexB < 0) continue;
            var forward = a.X <= b.X;
            var x1 = a.X + (forward ? 255 : 0);
            var x2 = b.X + (forward ? 0 : 255);
            var y1 = a.Y + 42 + indexA * 28 + 14;
            var y2 = b.Y + 42 + indexB * 28 + 14;
            var delta = forward ? 75 : -75;
            var figure = new PathFigure { StartPoint = new Point(x1, y1) };
            figure.Segments.Add(new BezierSegment(new Point(x1 + delta, y1), new Point(x2 - delta, y2), new Point(x2, y2), true));
            var path = new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { figure }), Stroke = rel.IsActive ? Brushes.Teal : Brushes.Gray,
                StrokeThickness = rel.IsActive ? 2.2 : 1.2, IsHitTestVisible = false };
            _canvas.Children.Insert(0, path);
            _edgeElements.Add(path);
            var badge = new Border { Background = Brushes.Teal, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2), IsHitTestVisible = false,
                Child = new TextBlock { Text = rel.Cardinality switch
                {
                    "OneToOne" => "1:1", "OneToMany" => "1:N", "ManyToOne" => "N:1", _ => "N:N"
                }, Foreground = Brushes.White, FontSize = 11 } };
            Canvas.SetLeft(badge, (x1 + x2) / 2 - 12);
            Canvas.SetTop(badge, (y1 + y2) / 2 - 12);
            _canvas.Children.Insert(0, badge);
            _edgeElements.Add(badge);
        }
    }

    private void RefreshRelationships()
    {
        _relationships.ItemsSource = _current.Relationships.Select(r =>
        {
            var a = _current.Nodes.FirstOrDefault(x => x.Id == r.FromNodeId);
            var b = _current.Nodes.FirstOrDefault(x => x.Id == r.ToNodeId);
            return new RelationChoice
            {
                Relationship = r,
                Caption = $"{a?.ObjectName}.{r.FromColumn} → {b?.ObjectName}.{r.ToColumn} ({r.Cardinality})"
            };
        }).ToArray();
    }

    private void SaveModel()
    {
        try
        {
            _current.Name = _name.Text.Trim();
            var old = _saved.FirstOrDefault(x => x.Code.Equals(_current.Code, StringComparison.OrdinalIgnoreCase));
            var updated = _saved.Where(x => !x.Code.Equals(_current.Code, StringComparison.OrdinalIgnoreCase))
                .Select(DmsGraphModelCatalog.Clone).ToList();
            updated.Add(DmsGraphModelCatalog.Clone(_current));
            var fingerprint = _catalog.Save(updated, _fingerprint);
            AuditModelChanges(old, _current);
            _saved = updated.OrderBy(x => x.Name).ToList();
            _fingerprint = fingerprint;
            _dirty = false;
            RefreshModelChoice();
            SetStatus(T("SYS15.Graph.Saved", "Datový model byl uložen."));
            _logger.AdminAction("SYS15", "GRAPH_MODEL_SAVE", _user,
                $"Model={_current.Code}; Nodes={_current.Nodes.Count}; Relationships={_current.Relationships.Count}");
        }
        catch (Exception ex) { SetStatus(ex.Message); _logger.Error("SYS15 graph model save failed", ex); }
    }

    private void AuditModelChanges(DmsGraphModelDefinition? old, DmsGraphModelDefinition current)
    {
        if (old is null) _logger.AuditCreated("SYS15", "GraphModel", current.Code, _user, $"Name={current.Name}");
        else if (old.Name != current.Name) _logger.AuditChange("SYS15", "GraphModel", current.Code, "Name", old.Name, current.Name, _user);
        var formerNodes = (old?.Nodes ?? new()).ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var node in current.Nodes)
        {
            if (!formerNodes.TryGetValue(node.Id, out var prior))
                _logger.AuditCreated("SYS15", "GraphNode", node.Id, _user, $"Model={current.Code}; Source={node.SourceCode}; Object={node.ObjectName}");
            else if (prior.X != node.X || prior.Y != node.Y)
                _logger.AuditChange("SYS15", "GraphNode", node.Id, "Position", $"{prior.X:0},{prior.Y:0}", $"{node.X:0},{node.Y:0}", _user);
        }
        foreach (var node in formerNodes.Values.Where(n => current.Nodes.All(x => x.Id != n.Id)))
            _logger.AuditDeleted("SYS15", "GraphNode", node.Id, _user, $"Model={current.Code}; Object={node.ObjectName}");
        var formerEdges = (old?.Relationships ?? new()).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in current.Relationships)
            if (!formerEdges.ContainsKey(edge.Id))
                _logger.AuditCreated("SYS15", "GraphRelationship", edge.Id, _user,
                    $"Model={current.Code}; From={edge.FromNodeId}.{edge.FromColumn}; To={edge.ToNodeId}.{edge.ToColumn}; Cardinality={edge.Cardinality}; Direction={edge.FilterDirection}");
        foreach (var edge in formerEdges.Values.Where(x => current.Relationships.All(y => y.Id != x.Id)))
            _logger.AuditDeleted("SYS15", "GraphRelationship", edge.Id, _user, $"Model={current.Code}");
    }

    private void DeleteModel()
    {
        var old = _saved.FirstOrDefault(x => x.Code == _current.Code);
        if (old is null) { NewModel(false); return; }
        if (MessageBox.Show(T("SYS15.Graph.DeleteConfirm", "Trvale odstranit uložený datový model?"), "SYS15",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var remaining = _saved.Where(x => x.Code != old.Code).ToList();
            _fingerprint = _catalog.Save(remaining, _fingerprint);
            foreach (var link in old.Relationships)
                _logger.AuditDeleted("SYS15", "GraphRelationship", link.Id, _user, $"Model={old.Code}");
            foreach (var node in old.Nodes)
                _logger.AuditDeleted("SYS15", "GraphNode", node.Id, _user, $"Model={old.Code}; Object={node.ObjectName}");
            _logger.AuditDeleted("SYS15", "GraphModel", old.Code, _user, $"Name={old.Name}");
            _saved = remaining;
            if (_saved.Count > 0) OpenModel(_saved[0]); else NewModel(false);
            RefreshModelChoice();
            SetStatus(T("SYS15.Graph.Deleted", "Model odstraněn."));
        }
        catch (Exception ex) { SetStatus(ex.Message); _logger.Error("SYS15 graph model delete failed", ex); }
    }
}
