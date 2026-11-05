using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class CalculatedColumnsStore
    {
        public Dictionary<string, List<Mes06CalculatedColumn>> Reports { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private string CalculatedColumnsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DMS", "MES06", "calculated-columns.json");

    private CalculatedColumnsStore ReadCalculatedColumns()
    {
        try
        {
            if (File.Exists(CalculatedColumnsPath))
                return JsonSerializer.Deserialize<CalculatedColumnsStore>(File.ReadAllText(CalculatedColumnsPath))
                       ?? new CalculatedColumnsStore();
        }
        catch (Exception ex) { _logger.Error("MES06 calculated columns could not be loaded.", ex); }
        return new CalculatedColumnsStore();
    }

    private void SaveCalculatedColumns(CalculatedColumnsStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CalculatedColumnsPath)!);
        File.WriteAllText(CalculatedColumnsPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class CalculatedCellConverter : IValueConverter
    {
        private readonly IReadOnlyList<Mes06CalculatedColumn> _columns;
        private readonly string _key;
        public CalculatedCellConverter(IReadOnlyList<Mes06CalculatedColumn> columns, string key)
        { _columns = columns; _key = key; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            if (!Mes06CalculatedColumnEvaluator.TryEvaluateRow(value, _columns, out var results, out _))
                return "—";
            var definition = _columns.FirstOrDefault(c => string.Equals(c.Key, _key, StringComparison.OrdinalIgnoreCase));
            return definition != null && results.TryGetValue(_key, out var result)
                ? result.ToString("N" + Math.Clamp(definition.DecimalPlaces, 0, 6), culture)
                : string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private void AppendCalculatedColumns(DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition definition)
    {
        var store = ReadCalculatedColumns();
        if (!store.Reports.TryGetValue(definition.Code ?? string.Empty, out var columns) || columns.Count == 0)
            return;
        foreach (var column in columns.Where(c => c.Visible))
        {
            GridReport.Columns.Add(new DataGridTextColumn
            {
                Header = string.IsNullOrWhiteSpace(column.Header) ? column.Key : column.Header,
                Binding = new Binding(".") { Converter = new CalculatedCellConverter(columns, column.Key), Mode = BindingMode.OneWay },
                Width = new DataGridLength(120)
            });
        }
    }

    // MES06_V5_BASE_COLUMNS: Keep source columns when a report definition lacks them.
    // Do not change the original ItemsSource or the built-in Plachta/OEE calculations.
    private void EnsureBaseReportColumns(DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition definition)
    {
        var existingBase = GridReport.Columns
            .OfType<DataGridTextColumn>()
            .Where(c => c.Binding is Binding b && b.Path?.Path != ".")
            .ToList();
        if (existingBase.Any(c => c.Visibility == Visibility.Visible))
            return;

        // When all source columns are hidden by a saved layout, restore them
        // rather than presenting a table containing only calculated fields.
        if (existingBase.Count > 0)
        {
            foreach (var col in existingBase)
                col.Visibility = Visibility.Visible;
            return;
        }

        var sample = _currentRows.FirstOrDefault();
        if (sample == null) return;
        foreach (var property in sample.GetType().GetProperties(
                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!(type.IsPrimitive || type.IsEnum || type == typeof(decimal) ||
                  type == typeof(string) || type == typeof(DateTime) || type == typeof(Guid)))
                continue;
            GridReport.Columns.Add(new DataGridTextColumn
            {
                Header = property.Name,
                Binding = new Binding(property.Name) { Mode = BindingMode.OneWay },
                Width = new DataGridLength(130)
            });
        }
    }

    private void OpenCalculatedColumnsEditor(DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition definition)
    {
        var store = ReadCalculatedColumns();
        var reportCode = definition.Code ?? string.Empty;
        var source = store.Reports.TryGetValue(reportCode, out var existing) ? existing : new List<Mes06CalculatedColumn>();
        var items = new ObservableCollection<Mes06CalculatedColumn>(source.Select(c => new Mes06CalculatedColumn
        { Key = c.Key, Header = c.Header, Formula = c.Formula, DecimalPlaces = c.DecimalPlaces, Visible = c.Visible }));

        var background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 27, 39));
        var surface = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 42, 57));
        var foreground = System.Windows.Media.Brushes.WhiteSmoke;
        var muted = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(175, 195, 211));
        var accent = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 108, 199));
        var window = new Window
        {
            Title = "MES06 – vypočítané sloupce: " + reportCode,
            Width = 1150, Height = 690, MinWidth = 900, MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            Background = background, Foreground = foreground
        };
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        root.Children.Add(columns);

        var left = new DockPanel(); Grid.SetColumn(left, 0); columns.Children.Add(left);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        var grid = new DataGrid
        {
            ItemsSource = items, AutoGenerateColumns = false, CanUserAddRows = false,
            IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
            Background = surface, Foreground = foreground, RowBackground = surface,
            AlternatingRowBackground = background, BorderThickness = new Thickness(0),
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Klíč", Binding = new Binding(nameof(Mes06CalculatedColumn.Key)), Width = new DataGridLength(125) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Název", Binding = new Binding(nameof(Mes06CalculatedColumn.Header)), Width = new DataGridLength(155) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Výraz", Binding = new Binding(nameof(Mes06CalculatedColumn.Formula)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        left.Children.Add(grid);

        var right = new DockPanel(); Grid.SetColumn(right, 2); columns.Children.Add(right);
        var title = new TextBlock { Text = "Vlastnosti sloupce", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = foreground, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(title, Dock.Top); right.Children.Add(title);
        var detailsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true
        };
        var details = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
        detailsScroll.Content = details;
        right.Children.Add(detailsScroll);
        TextBox Field(string label, int height = 34)
        {
            details.Children.Add(new TextBlock { Text = label, Foreground = muted, Margin = new Thickness(0, 9, 0, 4) });
            var field = new TextBox { Height = height, Padding = new Thickness(8, 5, 8, 5), Background = surface, Foreground = foreground,
                BorderBrush = muted, VerticalContentAlignment = VerticalAlignment.Center };
            details.Children.Add(field); return field;
        }
        var keyBox = Field("Klíč (jedinečný identifikátor)");
        var nameBox = Field("Název sloupce");
        var formulaBox = Field("Výraz", 80);
        formulaBox.AcceptsReturn = true; formulaBox.TextWrapping = TextWrapping.Wrap;
        var settings = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 8) };
        details.Children.Add(settings);
        settings.Children.Add(new TextBlock { Text = "Desetinná místa:", Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        var decimals = new ComboBox { Width = 65, Margin = new Thickness(10, 0, 25, 0), Background = surface, Foreground = foreground };
        for (var n = 0; n <= 6; n++) decimals.Items.Add(n);
        settings.Children.Add(decimals);
        var visible = new CheckBox { Content = "Zobrazit v sestavě", Foreground = foreground, VerticalAlignment = VerticalAlignment.Center };
        settings.Children.Add(visible);
        var status = new TextBlock { Text = "Vyber sloupec a zadej výraz.", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
        details.Children.Add(status);
        var preview = new TextBlock { Text = "", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        details.Children.Add(preview);
        details.Children.Add(new TextBlock { Text = "Dostupná číselná data (dvojklik vloží do výrazu)", Foreground = foreground, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5) });
        var dataList = new ListBox { Height = 135, Background = surface, Foreground = foreground, BorderThickness = new Thickness(0) };
        details.Children.Add(dataList);
        var hint = new TextBlock { Text = "Operátory: +  −  *  /  (  ). Lze použít i klíče předchozích vypočítaných sloupců.", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        details.Children.Add(hint);

        Mes06CalculatedColumn? selected = null;
        bool updating = false;
        void SyncFields()
        {
            if (updating || selected == null) return;
            selected.Key = keyBox.Text.Trim(); selected.Header = nameBox.Text;
            selected.Formula = formulaBox.Text;
            selected.DecimalPlaces = decimals.SelectedItem is int n ? n : 1;
            selected.Visible = visible.IsChecked == true;
        }
        void Validate()
        {
            SyncFields();
            if (selected == null) return;
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in items)
            {
                if (string.IsNullOrWhiteSpace(col.Key) || !(char.IsLetter(col.Key[0]) || col.Key[0] == '_') ||
                    col.Key.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') || !keys.Add(col.Key))
                { status.Text = "✕ Neplatný nebo duplicitní klíč: " + col.Key; status.Foreground = System.Windows.Media.Brushes.OrangeRed; return; }
                if (string.IsNullOrWhiteSpace(col.Formula))
                { status.Text = "✕ Chybí výraz pro: " + col.Key; status.Foreground = System.Windows.Media.Brushes.OrangeRed; return; }
            }
            var sample = _currentRows.FirstOrDefault();
            if (sample == null)
            { status.Text = "ℹ Pro ověření výsledku nejprve načti sestavu. Výraz bude zkontrolován při načtení dat."; status.Foreground = muted; preview.Text = ""; return; }
            if (Mes06CalculatedColumnEvaluator.TryEvaluateRow(sample, items.ToList(), out var values, out var error))
            {
                status.Text = "✓ Výrazy jsou platné pro první řádek sestavy.";
                status.Foreground = System.Windows.Media.Brushes.LightGreen;
                preview.Text = selected != null && values.TryGetValue(selected.Key, out var result)
                    ? "Ukázkový výsledek: " + result.ToString("N" + Math.Clamp(selected.DecimalPlaces, 0, 6), CultureInfo.CurrentCulture)
                    : "";
            }
            else { status.Text = "✕ " + error; status.Foreground = System.Windows.Media.Brushes.OrangeRed; preview.Text = ""; }
        }
        keyBox.TextChanged += (_, _) => Validate();
        nameBox.TextChanged += (_, _) => SyncFields();
        formulaBox.TextChanged += (_, _) => Validate();
        decimals.SelectionChanged += (_, _) => Validate();
        visible.Checked += (_, _) => SyncFields(); visible.Unchecked += (_, _) => SyncFields();
        grid.SelectionChanged += (_, _) =>
        {
            selected = grid.SelectedItem as Mes06CalculatedColumn;
            updating = true;
            keyBox.Text = selected?.Key ?? ""; nameBox.Text = selected?.Header ?? "";
            formulaBox.Text = selected?.Formula ?? "";
            decimals.SelectedItem = selected?.DecimalPlaces ?? 1;
            visible.IsChecked = selected?.Visible ?? false;
            updating = false;
            Validate();
        };
        dataList.MouseDoubleClick += (_, _) =>
        {
            if (selected == null || dataList.SelectedItem is not string item) return;
            if (item.Contains("vzorec: NE")) return;
            var key = item.Split(' ')[0];
            formulaBox.SelectedText = key; formulaBox.Focus();
        };
        void RefreshAvailableData()
        {
            dataList.Items.Clear();
            // Catalog is based on the current report schema, not only the first row.
            // Numeric values alone can be referenced by the formula engine.
            var sample = _currentRows.FirstOrDefault();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var properties = sample?.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                ?? Array.Empty<System.Reflection.PropertyInfo>();
            foreach (var property in properties.OrderBy(p => p.Name))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var numeric = type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
                              type == typeof(long) || type == typeof(float) || type == typeof(double) ||
                              type == typeof(decimal) || type == typeof(uint) || type == typeof(ushort) ||
                              type == typeof(ulong) || type == typeof(sbyte);
                object? value = null;
                try { value = property.GetValue(sample); } catch { /* diagnostic catalog only */ }
                var description = definition.Columns.FirstOrDefault(c => string.Equals(c.Property, property.Name, StringComparison.OrdinalIgnoreCase))?.Header;
                var label = property.Name + "  |  " + (description ?? property.Name) + "  |  " +
                            type.Name + "  |  " + (numeric ? "vzorec: ANO" : "vzorec: NE") +
                            "  |  " + (value?.ToString() ?? "null");
                dataList.Items.Add(label);
                seen.Add(property.Name);
            }
            foreach (var column in definition.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Property) && !seen.Contains(c.Property)))
                dataList.Items.Add(column.Property + "  |  " + column.Header + "  |  definice sestavy (nenacteno)  |  vzorec: NE");
            foreach (var col in items)
                dataList.Items.Add(col.Key + "  |  " + col.Header + "  |  vypocitany sloupec  |  vzorec: ANO");
        }        Button ActionButton(Panel parent, string caption, Action action, bool primary = false)
        {
            var button = new Button { Content = caption, MinWidth = 90, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0),
                Background = primary ? accent : surface, Foreground = foreground, BorderBrush = muted };
            button.Click += (_, _) => action(); parent.Children.Add(button); return button;
        }
        ActionButton(toolbar, "+ Přidat", () =>
        {
            var i = 1; while (items.Any(c => string.Equals(c.Key, "NewColumn" + i, StringComparison.OrdinalIgnoreCase))) i++;
            var column = new Mes06CalculatedColumn { Key = "NewColumn" + i, Header = "Nový sloupec", Formula = "0", DecimalPlaces = 1, Visible = true };
            items.Add(column); grid.SelectedItem = column; RefreshAvailableData();
        }, true);
        ActionButton(toolbar, "Duplikovat", () =>
        {
            if (selected == null) return;
            var i = 1; while (items.Any(c => string.Equals(c.Key, selected.Key + "_" + i, StringComparison.OrdinalIgnoreCase))) i++;
            var copy = new Mes06CalculatedColumn { Key = selected.Key + "_" + i, Header = selected.Header + " (kopie)", Formula = selected.Formula, DecimalPlaces = selected.DecimalPlaces, Visible = selected.Visible };
            items.Add(copy); grid.SelectedItem = copy; RefreshAvailableData();
        });
        ActionButton(toolbar, "Odebrat", () => { if (selected != null) { items.Remove(selected); RefreshAvailableData(); grid.SelectedIndex = items.Count > 0 ? 0 : -1; } });
        ActionButton(footer, "Zrušit", () => window.DialogResult = false);
        ActionButton(footer, "Ověřit", Validate);
        ActionButton(footer, "Uložit", () =>
        {
            SyncFields(); Validate();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in items)
            {
                if (string.IsNullOrWhiteSpace(col.Key) || !(char.IsLetter(col.Key[0]) || col.Key[0] == '_') ||
                    col.Key.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') || !keys.Add(col.Key) ||
                    string.IsNullOrWhiteSpace(col.Formula) || col.DecimalPlaces < 0 || col.DecimalPlaces > 6)
                { MessageBox.Show(window, "Oprav neplatný klíč, duplicitní klíč, výraz nebo desetinná místa.", "MES06"); return; }
            }
            var sample = _currentRows.FirstOrDefault();
            if (sample != null && !Mes06CalculatedColumnEvaluator.TryEvaluateRow(sample, items.ToList(), out _, out var error))
            { MessageBox.Show(window, error, "MES06 – chyba výpočtu"); return; }
            try
            {
                var before = source.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
                var after = items.ToList();
                store.Reports[reportCode] = after;
                SaveCalculatedColumns(store);
                foreach (var col in after)
                {
                    var action = before.ContainsKey(col.Key) ? "AUDIT" : "AUDIT_CREATE";
                    _logger.AdminAction("MES06", action, _user, $"Calculated column; Report={reportCode}; Key={col.Key}; Header={col.Header}; Formula={col.Formula}; Visible={col.Visible}; DecimalPlaces={col.DecimalPlaces}");
                }
                foreach (var col in source.Where(c => !after.Any(a => string.Equals(a.Key, c.Key, StringComparison.OrdinalIgnoreCase))))
                    _logger.AdminAction("MES06", "AUDIT_DELETE", _user, $"Calculated column; Report={reportCode}; Key={col.Key}; Formula={col.Formula}");
                window.DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "MES06"); }
        }, true);
        RefreshAvailableData();
        if (items.Count > 0) grid.SelectedIndex = 0;
        window.Content = root;
        if (window.ShowDialog() == true)
        {
            ApplyGridPresentation(definition);
            ApplyReportLayoutToExistingGrid(definition);
            EnsureBaseReportColumns(definition);
            AppendCalculatedColumns(definition);
        }
    }
}
