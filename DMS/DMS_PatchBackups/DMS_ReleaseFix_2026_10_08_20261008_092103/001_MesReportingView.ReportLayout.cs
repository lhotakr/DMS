using DMS.Integration.Mes.Reporting.Definitions;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class Mes06ReportLayoutDocument
    {
        public int Version { get; set; } = 1;
        public Mes06GlobalReportLayout Global { get; set; } = new();
        public Dictionary<string, Mes06ReportLayoutSettings> Reports { get; set; } = new();
    }

    private sealed class Mes06GlobalReportLayout
    {
        public double GridFontSize { get; set; } = 12d;
        public double PrintFontSize { get; set; } = 7.3d;
        public double PageMarginLeft { get; set; } = 30d;
        public double PageMarginTop { get; set; } = 30d;
        public double PageMarginRight { get; set; } = 30d;
        public double PageMarginBottom { get; set; } = 30d;
        public string DefaultAlignment { get; set; } = "Left";
        public bool RepeatColumnHeaders { get; set; } = true;
        public int RowsPerPrintPage { get; set; } = 30;
    }

    private sealed class Mes06ReportLayoutSettings
    {
        public Dictionary<string, Mes06ColumnLayoutSettings> Columns { get; set; } = new();
    }

    private sealed class Mes06ColumnLayoutSettings
    {
        public bool Visible { get; set; } = true;
        public int Order { get; set; }
        public double Width { get; set; } = 120d;
        public double FontSize { get; set; }
        public bool Bold { get; set; }
        public bool Underline { get; set; }
        public string Alignment { get; set; } = string.Empty;
    }

    private sealed class Mes06ColumnEditorItem
    {
        public string Property { get; init; } = string.Empty;
        public string Header { get; init; } = string.Empty;
        public bool Visible { get; set; }
        public int Order { get; set; }
        public double Width { get; set; }
        public double FontSize { get; set; }
        public bool Bold { get; set; }
        public bool Underline { get; set; }
        public string Alignment { get; set; } = "Left";
    }

    private sealed class Mes06EffectiveColumn
    {
        public required MesReportColumnDefinition Definition { get; init; }
        public required Mes06ColumnLayoutSettings Layout { get; init; }
    }

    private readonly JsonSerializerOptions _mes06ReportLayoutJsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private Mes06ReportLayoutDocument _mes06ReportLayoutDocument = new();
    private string _mes06ReportLayoutPath = string.Empty;
    private bool _mes06ReportLayoutLoaded;
    private Button? _mes06ReportSettingsButton;

    private void InitializeReportLayoutUi()
    {
        EnsureReportLayoutLoaded();

        if (_mes06ReportSettingsButton is not null)
        {
            return;
        }

        if (BtnToolbarPrint.Parent is not Panel panel)
        {
            return;
        }

        var button =
            new Button
            {
                Content = "⚙",
                Width = 42,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 19,
                ToolTip = T(
                    "MES06.ReportLayout.Tooltip",
                    "Výběr sloupců a nastavení reportu")
            };

        button.Click +=
            BtnReportSettings_Click;

        var index =
            panel.Children.IndexOf(
                BtnToolbarPrint);

        if (index < 0)
        {
            panel.Children.Add(
                button);
        }
        else
        {
            panel.Children.Insert(
                index,
                button);
        }

        _mes06ReportSettingsButton =
            button;
    }

    private void EnsureReportLayoutLoaded()
    {
        if (_mes06ReportLayoutLoaded)
        {
            return;
        }

        _mes06ReportLayoutLoaded =
            true;

        var root =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "DMS",
                "MES06");

        Directory.CreateDirectory(
            root);

        var invalid =
            Path.GetInvalidFileNameChars();

        var rawUser =
            string.IsNullOrWhiteSpace(
                _user)
                ? "default"
                : _user.Trim();

        var safeUser =
            new string(
                rawUser
                    .Select(ch =>
                        invalid.Contains(ch)
                            ? '_'
                            : ch)
                    .ToArray());

        _mes06ReportLayoutPath =
            Path.Combine(
                root,
                $"report-layout-{safeUser}.json");

        try
        {
            if (File.Exists(
                    _mes06ReportLayoutPath))
            {
                var json =
                    File.ReadAllText(
                        _mes06ReportLayoutPath);

                _mes06ReportLayoutDocument =
                    JsonSerializer.Deserialize<Mes06ReportLayoutDocument>(
                        json,
                        _mes06ReportLayoutJsonOptions)
                    ?? new Mes06ReportLayoutDocument();
            }
        }
        catch (Exception ex)
        {
            _mes06ReportLayoutDocument =
                new Mes06ReportLayoutDocument();

            _logger.Error(
                "MES06 report-layout settings could not be loaded.",
                ex);
        }

        NormalizeReportLayoutDocument();
    }

    private void NormalizeReportLayoutDocument()
    {
        _mes06ReportLayoutDocument.Global ??=
            new Mes06GlobalReportLayout();

        _mes06ReportLayoutDocument.Reports =
            new Dictionary<string, Mes06ReportLayoutSettings>(
                _mes06ReportLayoutDocument.Reports
                ?? new Dictionary<string, Mes06ReportLayoutSettings>(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var report in
                 _mes06ReportLayoutDocument.Reports.Values)
        {
            report.Columns =
                new Dictionary<string, Mes06ColumnLayoutSettings>(
                    report.Columns
                    ?? new Dictionary<string, Mes06ColumnLayoutSettings>(),
                    StringComparer.OrdinalIgnoreCase);
        }

        var global =
            _mes06ReportLayoutDocument.Global;

        global.GridFontSize =
            ClampLayout(
                global.GridFontSize,
                7d,
                28d,
                12d);

        global.PrintFontSize =
            ClampLayout(
                global.PrintFontSize,
                5d,
                20d,
                7.3d);

        global.PageMarginLeft =
            ClampLayout(
                global.PageMarginLeft,
                0d,
                150d,
                30d);
        global.PageMarginTop =
            ClampLayout(
                global.PageMarginTop,
                0d,
                150d,
                30d);
        global.PageMarginRight =
            ClampLayout(
                global.PageMarginRight,
                0d,
                150d,
                30d);
        global.PageMarginBottom =
            ClampLayout(
                global.PageMarginBottom,
                0d,
                150d,
                30d);

        global.RowsPerPrintPage =
            Math.Clamp(
                global.RowsPerPrintPage,
                0,
                120);

        global.DefaultAlignment =
            NormalizeAlignment(
                global.DefaultAlignment);
    }

    private void SaveReportLayoutDocument()
    {
        EnsureReportLayoutLoaded();

        try
        {
            var directory =
                Path.GetDirectoryName(
                    _mes06ReportLayoutPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            var json =
                JsonSerializer.Serialize(
                    _mes06ReportLayoutDocument,
                    _mes06ReportLayoutJsonOptions);

            File.WriteAllText(
                _mes06ReportLayoutPath,
                json);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 report-layout settings could not be saved.",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private Mes06ReportLayoutSettings GetReportLayout(
        MesReportDefinition definition)
    {
        EnsureReportLayoutLoaded();

        var code =
            string.IsNullOrWhiteSpace(
                definition.Code)
                ? "DEFAULT"
                : definition.Code.Trim();

        if (!_mes06ReportLayoutDocument.Reports.TryGetValue(
                code,
                out var reportLayout))
        {
            reportLayout =
                new Mes06ReportLayoutSettings
                {
                    Columns =
                        new Dictionary<string, Mes06ColumnLayoutSettings>(
                            StringComparer.OrdinalIgnoreCase)
                };

            _mes06ReportLayoutDocument.Reports[code] =
                reportLayout;
        }

        reportLayout.Columns =
            new Dictionary<string, Mes06ColumnLayoutSettings>(
                reportLayout.Columns
                ?? new Dictionary<string, Mes06ColumnLayoutSettings>(),
                StringComparer.OrdinalIgnoreCase);

        var order = 0;

        foreach (var column in
                 definition.Columns.Where(column =>
                     !string.IsNullOrWhiteSpace(
                         column.Property)))
        {
            EnsureColumnLayout(
                reportLayout,
                column.Property,
                column.Width,
                order++);
        }

        return reportLayout;
    }

    private static Mes06ColumnLayoutSettings EnsureColumnLayout(
        Mes06ReportLayoutSettings reportLayout,
        string property,
        double defaultWidth,
        int defaultOrder)
    {
        if (!reportLayout.Columns.TryGetValue(
                property,
                out var layout))
        {
            layout =
                new Mes06ColumnLayoutSettings
                {
                    Visible = true,
                    Order = defaultOrder,
                    Width =
                        Math.Max(
                            60d,
                            defaultWidth)
                };

            reportLayout.Columns[property] =
                layout;
        }

        if (layout.Width <= 0d)
        {
            layout.Width =
                Math.Max(
                    60d,
                    defaultWidth);
        }

        return layout;
    }

    private IReadOnlyList<Mes06EffectiveColumn> GetEffectiveReportColumns(
        MesReportDefinition definition)
    {
        var reportLayout =
            GetReportLayout(
                definition);

        return definition.Columns
            .Where(column =>
                !string.IsNullOrWhiteSpace(
                    column.Property))
            .Select(
                (column, defaultOrder) =>
                    new Mes06EffectiveColumn
                    {
                        Definition = column,
                        Layout =
                            EnsureColumnLayout(
                                reportLayout,
                                column.Property,
                                column.Width,
                                defaultOrder)
                    })
            .Where(item =>
                item.Layout.Visible)
            .OrderBy(item =>
                item.Layout.Order)
            .ThenBy(item =>
                definition.Columns.IndexOf(
                    item.Definition))
            .ToList();
    }

    private void BuildColumns(
        MesReportDefinition definition)
    {
        var reportLayout =
            GetReportLayout(
                definition);

        GridReport.Columns.Clear();

        var defaultOrder = 0;

        foreach (var column in
                 definition.Columns)
        {
            if (string.IsNullOrWhiteSpace(
                    column.Property))
            {
                continue;
            }

            var layout =
                EnsureColumnLayout(
                    reportLayout,
                    column.Property,
                    column.Width,
                    defaultOrder++);

            var binding =
                new Binding(
                    column.Property)
                {
                    Mode =
                        BindingMode.OneWay
                };

            if (!string.IsNullOrWhiteSpace(
                    column.Format))
            {
                binding.StringFormat =
                    column.Format;
            }

            if (IsPlachtaReport(
                    definition)
                && ChkHideZeros.IsChecked == true)
            {
                binding.Converter =
                    PlachtaHideZeroConverter.Instance;
            }

            GridReport.Columns.Add(
                new DataGridTextColumn
                {
                    Header =
                        column.Header,
                    Binding =
                        binding,
                    Width =
                        new DataGridLength(
                            Math.Max(
                                40d,
                                layout.Width)),
                    Visibility =
                        layout.Visible
                            ? Visibility.Visible
                            : Visibility.Collapsed,
                    ElementStyle =
                        CreateReportCellStyle(
                            layout)
                });
        }

        ApplyReportLayoutToExistingGrid(
            definition);
    }

    private void ApplyReportLayoutToExistingGrid(
        MesReportDefinition definition)
    {
        var reportLayout =
            GetReportLayout(
                definition);

        var discovered =
            GridReport.Columns
                .Select(
                    (column, index) =>
                        new
                        {
                            Column = column,
                            Property =
                                ExtractGridColumnProperty(
                                    column),
                            DefaultOrder = index
                        })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.Property))
                .ToList();

        foreach (var item in
                 discovered)
        {
            var layout =
                EnsureColumnLayout(
                    reportLayout,
                    item.Property,
                    item.Column.Width.IsAbsolute
                        ? item.Column.Width.Value
                        : Math.Max(
                            60d,
                            item.Column.ActualWidth),
                    item.DefaultOrder);

            item.Column.Visibility =
                layout.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            item.Column.Width =
                new DataGridLength(
                    Math.Max(
                        40d,
                        layout.Width));

            if (item.Column
                is DataGridTextColumn textColumn)
            {
                textColumn.ElementStyle =
                    CreateReportCellStyle(
                        layout);
            }
        }

        var ordered =
            discovered
                .OrderBy(item =>
                    reportLayout.Columns[item.Property]
                        .Order)
                .ThenBy(item =>
                    item.DefaultOrder)
                .ToList();

        for (var index = 0;
             index < ordered.Count;
             index++)
        {
            try
            {
                ordered[index]
                    .Column
                    .DisplayIndex =
                    index;
            }
            catch
            {
                // WPF may briefly reject DisplayIndex while a dynamic report
                // is rebuilding columns. The next layout pass fixes it.
            }
        }
    }

    private Style CreateReportCellStyle(
        Mes06ColumnLayoutSettings layout)
    {
        var global =
            _mes06ReportLayoutDocument.Global;

        var style =
            new Style(
                typeof(TextBlock));

        style.Setters.Add(
            new Setter(
                TextBlock.FontSizeProperty,
                layout.FontSize > 0d
                    ? layout.FontSize
                    : global.GridFontSize));

        style.Setters.Add(
            new Setter(
                TextBlock.FontWeightProperty,
                layout.Bold
                    ? FontWeights.Bold
                    : FontWeights.Normal));

        if (layout.Underline)
        {
            style.Setters.Add(
                new Setter(
                    TextBlock.TextDecorationsProperty,
                    TextDecorations.Underline));
        }

        style.Setters.Add(
            new Setter(
                TextBlock.TextAlignmentProperty,
                ParseTextAlignment(
                    string.IsNullOrWhiteSpace(
                        layout.Alignment)
                        ? global.DefaultAlignment
                        : layout.Alignment)));

        style.Setters.Add(
            new Setter(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center));

        return style;
    }

    private void BtnReportSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (CmbReport.SelectedItem
            is not MesReportDefinition definition)
        {
            return;
        }

        ShowReportSettingsDialog(
            definition);
    }

    private void ShowReportSettingsDialog(
        MesReportDefinition definition)
    {
        EnsureReportLayoutLoaded();
        ApplyReportLayoutToExistingGrid(
            definition);

        var reportLayout =
            GetReportLayout(
                definition);

        var editorItems =
            new ObservableCollection<Mes06ColumnEditorItem>();

        var descriptors =
            new Dictionary<string, (string Header, double Width, int Order)>(
                StringComparer.OrdinalIgnoreCase);

        for (var index = 0;
             index < definition.Columns.Count;
             index++)
        {
            var column =
                definition.Columns[index];

            if (string.IsNullOrWhiteSpace(
                    column.Property))
            {
                continue;
            }

            descriptors[column.Property] =
                (
                    column.Header,
                    column.Width,
                    index
                );
        }

        for (var index = 0;
             index < GridReport.Columns.Count;
             index++)
        {
            var gridColumn =
                GridReport.Columns[index];

            var property =
                ExtractGridColumnProperty(
                    gridColumn);

            if (string.IsNullOrWhiteSpace(
                    property)
                || descriptors.ContainsKey(
                    property))
            {
                continue;
            }

            descriptors[property] =
                (
                    Convert.ToString(
                        gridColumn.Header)
                    ?? property,
                    gridColumn.Width.IsAbsolute
                        ? gridColumn.Width.Value
                        : Math.Max(
                            60d,
                            gridColumn.ActualWidth),
                    index
                );
        }

        foreach (var descriptor in
                 descriptors.OrderBy(item =>
                     EnsureColumnLayout(
                         reportLayout,
                         item.Key,
                         item.Value.Width,
                         item.Value.Order)
                     .Order))
        {
            var layout =
                EnsureColumnLayout(
                    reportLayout,
                    descriptor.Key,
                    descriptor.Value.Width,
                    descriptor.Value.Order);

            editorItems.Add(
                new Mes06ColumnEditorItem
                {
                    Property =
                        descriptor.Key,
                    Header =
                        descriptor.Value.Header,
                    Visible =
                        layout.Visible,
                    Order =
                        layout.Order,
                    Width =
                        layout.Width,
                    FontSize =
                        layout.FontSize > 0d
                            ? layout.FontSize
                            : _mes06ReportLayoutDocument.Global.GridFontSize,
                    Bold =
                        layout.Bold,
                    Underline =
                        layout.Underline,
                    Alignment =
                        string.IsNullOrWhiteSpace(
                            layout.Alignment)
                            ? _mes06ReportLayoutDocument.Global.DefaultAlignment
                            : NormalizeAlignment(
                                layout.Alignment)
                });
        }

        var dialog =
            new Window
            {
                Title =
                    $"MES06 – Nastavení reportu: {definition.Name}",
                Width =
                    980,
                Height =
                    690,
                MinWidth =
                    800,
                MinHeight =
                    560,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Owner =
                    Window.GetWindow(
                        this)
            };

        var root =
            new Grid
            {
                Margin =
                    new Thickness(
                        14)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        var intro =
            new TextBlock
            {
                Text =
                    "Sloupce jsou uložené pro vybraný report. Nastavení písma, zarovnání a okrajů níže je globální pro MES06.",
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10),
                TextWrapping =
                    TextWrapping.Wrap
            };

        intro.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        Grid.SetRow(
            intro,
            0);
        root.Children.Add(
            intro);

        var tabs =
            new TabControl();

        Grid.SetRow(
            tabs,
            1);
        root.Children.Add(
            tabs);

        var columnsGrid =
            new DataGrid
            {
                ItemsSource =
                    editorItems,
                AutoGenerateColumns =
                    false,
                CanUserAddRows =
                    false,
                CanUserDeleteRows =
                    false,
                SelectionMode =
                    DataGridSelectionMode.Single,
                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        0)
            };

        columnsGrid.Columns.Add(
            new DataGridCheckBoxColumn
            {
                Header = "Zobrazit",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Visible))
            });

        columnsGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Pořadí",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Order)),
                Width =
                    70
            });

        columnsGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Sloupec",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Header)),
                IsReadOnly =
                    true,
                Width =
                    new DataGridLength(
                        1,
                        DataGridLengthUnitType.Star)
            });

        columnsGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Šířka",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Width))
                    {
                        StringFormat = "N0"
                    },
                Width =
                    80
            });

        columnsGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Písmo",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.FontSize))
                    {
                        StringFormat = "N1"
                    },
                Width =
                    75
            });

        columnsGrid.Columns.Add(
            new DataGridCheckBoxColumn
            {
                Header = "Tučně",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Bold))
            });

        columnsGrid.Columns.Add(
            new DataGridCheckBoxColumn
            {
                Header = "Podtržené",
                Binding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Underline))
            });

        columnsGrid.Columns.Add(
            new DataGridComboBoxColumn
            {
                Header = "Zarovnání",
                ItemsSource =
                    new[]
                    {
                        "Left",
                        "Center",
                        "Right"
                    },
                SelectedItemBinding =
                    new Binding(
                        nameof(Mes06ColumnEditorItem.Alignment)),
                Width =
                    105
            });

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    "Sloupce",
                Content =
                    columnsGrid
            });

        var global =
            _mes06ReportLayoutDocument.Global;

        var txtGridFont =
            CreateLayoutTextBox(
                global.GridFontSize);
        var txtPrintFont =
            CreateLayoutTextBox(
                global.PrintFontSize);
        var txtMarginLeft =
            CreateLayoutTextBox(
                global.PageMarginLeft);
        var txtMarginTop =
            CreateLayoutTextBox(
                global.PageMarginTop);
        var txtMarginRight =
            CreateLayoutTextBox(
                global.PageMarginRight);
        var txtMarginBottom =
            CreateLayoutTextBox(
                global.PageMarginBottom);
        var txtRowsPerPage =
            new TextBox
            {
                Width =
                    90,
                Text =
                    global.RowsPerPrintPage.ToString(
                        CultureInfo.CurrentCulture)
            };

        var cmbAlignment =
            new ComboBox
            {
                Width =
                    140,
                ItemsSource =
                    new[]
                    {
                        "Left",
                        "Center",
                        "Right"
                    },
                SelectedItem =
                    NormalizeAlignment(
                        global.DefaultAlignment)
            };

        var chkRepeatHeaders =
            new CheckBox
            {
                Content =
                    "Opakovat záhlaví sloupců na každé tiskové stránce",
                IsChecked =
                    global.RepeatColumnHeaders,
                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        6)
            };

        var settingsPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        12)
            };

        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Velikost písma v tabulce",
                txtGridFont));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Velikost písma v tisku / PDF",
                txtPrintFont));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Výchozí zarovnání textu",
                cmbAlignment));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Okraj vlevo",
                txtMarginLeft));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Okraj nahoře",
                txtMarginTop));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Okraj vpravo",
                txtMarginRight));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Okraj dole",
                txtMarginBottom));
        settingsPanel.Children.Add(
            CreateLayoutSettingRow(
                "Max. řádků na tiskovou stránku (0 = automaticky)",
                txtRowsPerPage));
        settingsPanel.Children.Add(
            chkRepeatHeaders);

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    "Report / tisk",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            settingsPanel
                    }
            });

        var buttons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        var btnReset =
            new Button
            {
                Content =
                    "Výchozí",
                MinWidth =
                    95,
                Height =
                    32,
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0)
            };

        var btnCancel =
            new Button
            {
                Content =
                    "Zrušit",
                MinWidth =
                    95,
                Height =
                    32,
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),
                IsCancel =
                    true
            };

        var btnSave =
            new Button
            {
                Content =
                    "Uložit",
                MinWidth =
                    95,
                Height =
                    32,
                IsDefault =
                    true
            };

        btnReset.Click +=
            (_, _) =>
            {
                for (var index = 0;
                     index < editorItems.Count;
                     index++)
                {
                    var item =
                        editorItems[index];

                    var descriptor =
                        descriptors[item.Property];

                    item.Visible =
                        true;
                    item.Order =
                        descriptor.Order;
                    item.Width =
                        Math.Max(
                            60d,
                            descriptor.Width);
                    item.FontSize =
                        12d;
                    item.Bold =
                        false;
                    item.Underline =
                        false;
                    item.Alignment =
                        "Left";
                }

                txtGridFont.Text =
                    "12";
                txtPrintFont.Text =
                    "7.3";
                txtMarginLeft.Text =
                    "30";
                txtMarginTop.Text =
                    "30";
                txtMarginRight.Text =
                    "30";
                txtMarginBottom.Text =
                    "30";
                txtRowsPerPage.Text =
                    "30";
                cmbAlignment.SelectedItem =
                    "Left";
                chkRepeatHeaders.IsChecked =
                    true;

                columnsGrid.Items.Refresh();
            };

        btnSave.Click +=
            (_, _) =>
            {
                columnsGrid.CommitEdit(
                    DataGridEditingUnit.Cell,
                    true);
                columnsGrid.CommitEdit(
                    DataGridEditingUnit.Row,
                    true);

                if (!editorItems.Any(item =>
                        item.Visible))
                {
                    DmsMessage.Show(
                        "Alespoň jeden sloupec musí zůstat viditelný.",
                        "MES06",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var updatedGlobal =
                    _mes06ReportLayoutDocument.Global;

                updatedGlobal.GridFontSize =
                    ReadLayoutDouble(
                        txtGridFont,
                        7d,
                        28d,
                        updatedGlobal.GridFontSize);
                updatedGlobal.PrintFontSize =
                    ReadLayoutDouble(
                        txtPrintFont,
                        5d,
                        20d,
                        updatedGlobal.PrintFontSize);
                updatedGlobal.PageMarginLeft =
                    ReadLayoutDouble(
                        txtMarginLeft,
                        0d,
                        150d,
                        updatedGlobal.PageMarginLeft);
                updatedGlobal.PageMarginTop =
                    ReadLayoutDouble(
                        txtMarginTop,
                        0d,
                        150d,
                        updatedGlobal.PageMarginTop);
                updatedGlobal.PageMarginRight =
                    ReadLayoutDouble(
                        txtMarginRight,
                        0d,
                        150d,
                        updatedGlobal.PageMarginRight);
                updatedGlobal.PageMarginBottom =
                    ReadLayoutDouble(
                        txtMarginBottom,
                        0d,
                        150d,
                        updatedGlobal.PageMarginBottom);

                if (int.TryParse(
                        txtRowsPerPage.Text,
                        NumberStyles.Integer,
                        CultureInfo.CurrentCulture,
                        out var rowsPerPage))
                {
                    updatedGlobal.RowsPerPrintPage =
                        Math.Clamp(
                            rowsPerPage,
                            0,
                            120);
                }

                updatedGlobal.DefaultAlignment =
                    NormalizeAlignment(
                        Convert.ToString(
                            cmbAlignment.SelectedItem));
                updatedGlobal.RepeatColumnHeaders =
                    chkRepeatHeaders.IsChecked == true;

                foreach (var item in
                         editorItems)
                {
                    var layout =
                        EnsureColumnLayout(
                            reportLayout,
                            item.Property,
                            item.Width,
                            item.Order);

                    layout.Visible =
                        item.Visible;
                    layout.Order =
                        item.Order;
                    layout.Width =
                        Math.Max(
                            40d,
                            item.Width);
                    layout.FontSize =
                        Math.Clamp(
                            item.FontSize,
                            7d,
                            28d);
                    layout.Bold =
                        item.Bold;
                    layout.Underline =
                        item.Underline;
                    layout.Alignment =
                        NormalizeAlignment(
                            item.Alignment);
                }

                SaveReportLayoutDocument();

                ApplyGridPresentation(
                    definition);
                ApplyReportLayoutToExistingGrid(
                    definition);

                _logger.AdminAction(
                    "MES06",
                    "AUDIT",
                    _user,
                    $"Report layout changed; Report={definition.Code}; VisibleColumns={editorItems.Count(item => item.Visible)}; RepeatHeaders={updatedGlobal.RepeatColumnHeaders}; RowsPerPage={updatedGlobal.RowsPerPrintPage}");

                dialog.DialogResult =
                    true;
            };

        buttons.Children.Add(
            btnReset);
        buttons.Children.Add(
            btnCancel);
        buttons.Children.Add(
            btnSave);

        Grid.SetRow(
            buttons,
            2);
        root.Children.Add(
            buttons);

        dialog.Content =
            root;

        dialog.ShowDialog();
    }

    private static TextBox CreateLayoutTextBox(
        double value) =>
        new()
        {
            Width =
                90,
            Text =
                value.ToString(
                    "0.##",
                    CultureInfo.CurrentCulture)
        };

    private static Grid CreateLayoutSettingRow(
        string label,
        Control editor)
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8)
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        260)
            });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var text =
            new TextBlock
            {
                Text =
                    label,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        0,
                        12,
                        0)
            };

        Grid.SetColumn(
            text,
            0);
        Grid.SetColumn(
            editor,
            1);

        grid.Children.Add(
            text);
        grid.Children.Add(
            editor);

        return grid;
    }

    private static double ReadLayoutDouble(
        TextBox textBox,
        double min,
        double max,
        double fallback)
    {
        if (!double.TryParse(
                textBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var value)
            && !double.TryParse(
                textBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return fallback;
        }

        return Math.Clamp(
            value,
            min,
            max);
    }

    private static double ClampLayout(
        double value,
        double min,
        double max,
        double fallback)
    {
        if (double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(
            value,
            min,
            max);
    }

    private static string NormalizeAlignment(
        string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "center" => "Center",
            "right" => "Right",
            _ => "Left"
        };
    }

    private static TextAlignment ParseTextAlignment(
        string? value) =>
        NormalizeAlignment(
            value) switch
        {
            "Center" =>
                TextAlignment.Center,
            "Right" =>
                TextAlignment.Right,
            _ =>
                TextAlignment.Left
        };

    private static string ExtractGridColumnProperty(
        DataGridColumn column)
    {
        if (column
            is not DataGridTextColumn textColumn
            || textColumn.Binding
                is not Binding binding)
        {
            return string.Empty;
        }

        var path =
            binding.Path?.Path
            ?? string.Empty;

        path =
            path.Trim();

        if (path.StartsWith(
                "[",
                StringComparison.Ordinal)
            && path.EndsWith(
                "]",
                StringComparison.Ordinal)
            && path.Length > 2)
        {
            path =
                path[1..^1];
        }

        return path;
    }

    private Mes06ColumnLayoutSettings? FindColumnLayout(
        MesReportDefinition definition,
        string property)
    {
        if (string.IsNullOrWhiteSpace(
                property))
        {
            return null;
        }

        var reportLayout =
            GetReportLayout(
                definition);

        return reportLayout.Columns.TryGetValue(
            property,
            out var layout)
            ? layout
            : null;
    }

    private MesReportPrintBehaviorDefinition GetReportPrintBehavior(
        MesReportDefinition? definition)
    {
        return definition?.Behavior?.Print
               ?? new MesReportPrintBehaviorDefinition();
    }

    private PageOrientation ResolvePrintOrientation(
        MesReportDefinition? definition)
    {
        var value =
            GetReportPrintBehavior(
                    definition)
                .Orientation;

        return string.Equals(
            value,
            "Portrait",
            StringComparison.OrdinalIgnoreCase)
            ? PageOrientation.Portrait
            : PageOrientation.Landscape;
    }

    private bool ResolveRepeatColumnHeaders(
        MesReportDefinition? definition)
    {
        EnsureReportLayoutLoaded();

        return _mes06ReportLayoutDocument.Global.RepeatColumnHeaders
               && GetReportPrintBehavior(
                       definition)
                   .RepeatColumnHeaders;
    }

    private int ResolveMaxRowsPerPrintPage(
        MesReportDefinition? definition)
    {
        EnsureReportLayoutLoaded();

        var userLimit =
            _mes06ReportLayoutDocument.Global.RowsPerPrintPage;

        var definitionLimit =
            GetReportPrintBehavior(
                    definition)
                .MaxRowsPerPage;

        if (userLimit > 0
            && definitionLimit > 0)
        {
            return Math.Min(
                userLimit,
                definitionLimit);
        }

        if (userLimit > 0)
        {
            return userLimit;
        }

        return Math.Max(
            0,
            definitionLimit);
    }

    private void BtnToolbarPrint_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentRows.Count == 0)
        {
            ShowNoPrintData();
            return;
        }

        try
        {
            var definition =
                CmbReport.SelectedItem
                    as MesReportDefinition;

            var orientation =
                ResolvePrintOrientation(
                    definition);

            var printDialog =
                new PrintDialog();

            if (printDialog.ShowDialog() !=
                true)
            {
                return;
            }

            if (printDialog.PrintTicket is not null)
            {
                printDialog.PrintTicket.PageOrientation =
                    orientation;
            }

            // Important: calculate the printer page BEFORE the FlowDocument is
            // built. The previous implementation rebuilt PageWidth/PageHeight
            // afterwards, while the table columns already had fixed widths.
            // Some drivers then rejected the paginator or clipped the report.
            var pageSize =
                ResolvePrinterPageSize(
                    printDialog,
                    orientation);

            var document =
                BuildPrintableDocument(
                    pageSize.Width,
                    pageSize.Height);

            var paginator =
                ((IDocumentPaginatorSource)document)
                    .DocumentPaginator;

            paginator.PageSize =
                pageSize;

            var printReportName =
                definition?.Name
                ?? T(
                    "MES06.Title",
                    "MES06");

            printDialog.PrintDocument(
                paginator,
                $"MES06 - {printReportName}");

            _logger.AdminAction(
                "MES06",
                "PrintReport",
                _user,
                $"Rows={_currentRows.Count}; Preset={_activePresetName}; Page={pageSize.Width:0}x{pageSize.Height:0}; Orientation={orientation}");
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 report printing failed.",
                ex);

            MessageBox.Show(
                string.Format(
                    T(
                        "MES06.Print.PrintFailed",
                        "The report could not be printed.{0}{1}"),
                    Environment.NewLine,
                    ex.Message),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static Size ResolvePrinterPageSize(
        PrintDialog printDialog,
        PageOrientation orientation)
    {
        var width =
            printDialog.PrintableAreaWidth;

        var height =
            printDialog.PrintableAreaHeight;

        try
        {
            if (printDialog.PrintQueue is not null)
            {
                var capabilities =
                    printDialog.PrintQueue
                        .GetPrintCapabilities(
                            printDialog.PrintTicket);

                var area =
                    capabilities.PageImageableArea;

                if (area is not null
                    && area.ExtentWidth > 0d
                    && area.ExtentHeight > 0d)
                {
                    width =
                        area.ExtentWidth;
                    height =
                        area.ExtentHeight;
                }
            }
        }
        catch
        {
            // Some legacy printer drivers do not expose capabilities reliably.
            // PrintableAreaWidth/Height remain a safe fallback.
        }

        if (width <= 0d
            || double.IsNaN(width)
            || double.IsInfinity(width))
        {
            width =
                orientation == PageOrientation.Portrait
                    ? 793d
                    : 1122d;
        }

        if (height <= 0d
            || double.IsNaN(height)
            || double.IsInfinity(height))
        {
            height =
                orientation == PageOrientation.Portrait
                    ? 1122d
                    : 793d;
        }

        if (orientation == PageOrientation.Landscape
            && width < height)
        {
            (width, height) =
                (height, width);
        }
        else if (orientation == PageOrientation.Portrait
                 && width > height)
        {
            (width, height) =
                (height, width);
        }

        return new Size(
            width,
            height);
    }

    private FlowDocument BuildPrintableDocument(
        double? pageWidth = null,
        double? pageHeight = null)
    {
        EnsureReportLayoutLoaded();

        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        var orientation =
            ResolvePrintOrientation(
                definition);

        var resolvedWidth =
            pageWidth
            ?? (orientation == PageOrientation.Portrait
                ? 793d
                : 1122d);

        var resolvedHeight =
            pageHeight
            ?? (orientation == PageOrientation.Portrait
                ? 1122d
                : 793d);

        var global =
            _mes06ReportLayoutDocument.Global;

        var document =
            new FlowDocument
            {
                PageWidth =
                    resolvedWidth,
                PageHeight =
                    resolvedHeight,
                PagePadding =
                    new Thickness(
                        global.PageMarginLeft,
                        global.PageMarginTop,
                        global.PageMarginRight,
                        global.PageMarginBottom),
                ColumnWidth =
                    10000d,
                FontFamily =
                    new FontFamily(
                        "Segoe UI"),
                FontSize =
                    global.PrintFontSize,
                Foreground =
                    Brushes.Black,
                Background =
                    Brushes.White,
                IsColumnWidthFlexible =
                    false
            };

        var definitionName =
            definition?.Name
            ?? T(
                "MES06.Report.Counter.Name",
                "Counter report");

        var title =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        4),
                FontSize =
                    17,
                FontWeight =
                    FontWeights.Bold
            };

        title.Inlines.Add(
            $"{TxtTitle.Text} – {definitionName}");

        document.Blocks.Add(
            title);

        var filters =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10),
                FontSize =
                    8.5
            };

        filters.Inlines.Add(
            BuildPrintFilterSummary());

        document.Blocks.Add(
            filters);

        AppendVisibleChartToDocument(
            document);

        AppendGridToDocument(
            document,
            GridReport,
            GetPrintableGridRows(
                GridReport));

        if (CounterSummaryBorder.Visibility ==
                Visibility.Visible
            && GridCounterSummary.ItemsSource
                is IEnumerable summaryRows)
        {
            var summaryTitle =
                new Paragraph
                {
                    Margin =
                        new Thickness(
                            0,
                            14,
                            0,
                            5),
                    FontSize =
                        12,
                    FontWeight =
                        FontWeights.Bold
                };

            summaryTitle.Inlines.Add(
                TxtCounterSummaryTitle.Text);

            document.Blocks.Add(
                summaryTitle);

            AppendGridToDocument(
                document,
                GridCounterSummary,
                summaryRows.Cast<object>().ToList());
        }

        var footer =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        0,
                        10,
                        0,
                        0),
                FontSize =
                    7,
                Foreground =
                    Brushes.DimGray
            };

        footer.Inlines.Add(
            string.Format(
                T(
                    "MES06.Print.Generated",
                    "Generated {0} by {1}"),
                DateTime.Now.ToString(
                    "dd.MM.yyyy HH:mm:ss"),
                _user));

        document.Blocks.Add(
            footer);

        return document;
    }

    private void AppendGridToDocument(
        FlowDocument document,
        DataGrid grid,
        IReadOnlyList<object> rows)
    {
        EnsureReportLayoutLoaded();

        var global =
            _mes06ReportLayoutDocument.Global;

        var definition =
            CmbReport.SelectedItem
                as MesReportDefinition;

        var behavior =
            GetReportPrintBehavior(
                definition);

        document.PagePadding =
            new Thickness(
                global.PageMarginLeft,
                global.PageMarginTop,
                global.PageMarginRight,
                global.PageMarginBottom);

        document.FontSize =
            global.PrintFontSize;

        var columns =
            grid.Columns
                .Where(column =>
                    column.Visibility ==
                    Visibility.Visible)
                .OrderBy(column =>
                    column.DisplayIndex)
                .ToList();

        if (columns.Count == 0)
        {
            return;
        }

        var repeatHeaders =
            ResolveRepeatColumnHeaders(
                definition);

        if (!repeatHeaders
            || !behavior.UseMeasuredPagination)
        {
            var maxRows =
                ResolveMaxRowsPerPrintPage(
                    definition);

            if (!repeatHeaders
                || maxRows <= 0
                || rows.Count <= maxRows)
            {
                document.Blocks.Add(
                    CreateConfiguredPrintTable(
                        document,
                        grid,
                        columns,
                        rows,
                        definition,
                        keepTogether: false));
                return;
            }
        }

        var chunks =
            BuildMeasuredPrintChunks(
                document,
                grid,
                columns,
                rows,
                definition);

        if (chunks.Count == 0)
        {
            chunks.Add(
                new List<object>());
        }

        for (var pageIndex = 0;
             pageIndex < chunks.Count;
             pageIndex++)
        {
            var table =
                CreateConfiguredPrintTable(
                    document,
                    grid,
                    columns,
                    chunks[pageIndex],
                    definition,
                    keepTogether: true);

            if (pageIndex == 0)
            {
                document.Blocks.Add(
                    table);
                continue;
            }

            var section =
                new Section
                {
                    BreakPageBefore =
                        true
                };

            section.Blocks.Add(
                table);

            document.Blocks.Add(
                section);
        }
    }

    private List<List<object>> BuildMeasuredPrintChunks(
        FlowDocument document,
        DataGrid grid,
        IReadOnlyList<DataGridColumn> columns,
        IReadOnlyList<object> rows,
        MesReportDefinition? definition)
    {
        var result =
            new List<List<object>>();

        if (rows.Count == 0)
        {
            return result;
        }

        var behavior =
            GetReportPrintBehavior(
                definition);

        var columnWidths =
            CalculateConfiguredPrintColumnWidths(
                document,
                columns);

        var headerHeight =
            EstimatePrintHeaderHeight(
                columns,
                columnWidths,
                definition,
                behavior.HeaderMinimumHeight);

        var pageHeight =
            Math.Max(
                200d,
                document.PageHeight
                - document.PagePadding.Top
                - document.PagePadding.Bottom
                - Math.Max(
                    0d,
                    behavior.PageSafetyMargin));

        var isPrimaryGrid =
            ReferenceEquals(
                grid,
                GridReport);

        var firstReserved =
            isPrimaryGrid
                ? Math.Max(
                    0d,
                    behavior.FirstPageReservedHeight)
                : 40d;

        if (isPrimaryGrid
            && ChartBorder.Visibility ==
                Visibility.Visible)
        {
            firstReserved +=
                290d;
        }

        var firstCapacity =
            Math.Max(
                headerHeight + 30d,
                pageHeight
                - firstReserved);

        var nextCapacity =
            pageHeight;

        var maxRows =
            ResolveMaxRowsPerPrintPage(
                definition);

        var current =
            new List<object>();

        var usedHeight =
            headerHeight;

        var currentCapacity =
            firstCapacity;

        foreach (var row in
                 rows)
        {
            var rowHeight =
                EstimatePrintRowHeight(
                    columns,
                    columnWidths,
                    row,
                    definition);

            var rowLimitReached =
                maxRows > 0
                && current.Count >= maxRows;

            var heightLimitReached =
                current.Count > 0
                && usedHeight + rowHeight > currentCapacity;

            if (rowLimitReached
                || heightLimitReached)
            {
                result.Add(
                    current);

                current =
                    new List<object>();

                usedHeight =
                    headerHeight;

                currentCapacity =
                    nextCapacity;
            }

            current.Add(
                row);

            usedHeight +=
                rowHeight;
        }

        if (current.Count > 0)
        {
            result.Add(
                current);
        }

        return result;
    }

    private IReadOnlyList<double> CalculateConfiguredPrintColumnWidths(
        FlowDocument document,
        IReadOnlyList<DataGridColumn> columns)
    {
        var totalWidth =
            columns.Sum(column =>
                Math.Max(
                    40d,
                    column.Width.IsAbsolute
                        ? column.Width.Value
                        : Math.Max(
                            40d,
                            column.ActualWidth)));

        var printableWidth =
            Math.Max(
                300d,
                document.PageWidth
                - document.PagePadding.Left
                - document.PagePadding.Right
                - 10d);

        return columns
            .Select(column =>
            {
                var sourceWidth =
                    Math.Max(
                        40d,
                        column.Width.IsAbsolute
                            ? column.Width.Value
                            : Math.Max(
                                40d,
                                column.ActualWidth));

                var weight =
                    totalWidth <= 0d
                        ? 1d / columns.Count
                        : sourceWidth / totalWidth;

                return printableWidth * weight;
            })
            .ToList();
    }

    private double EstimatePrintHeaderHeight(
        IReadOnlyList<DataGridColumn> columns,
        IReadOnlyList<double> columnWidths,
        MesReportDefinition? definition,
        double minimumHeight)
    {
        var height =
            Math.Max(
                18d,
                minimumHeight);

        for (var index = 0;
             index < columns.Count;
             index++)
        {
            height =
                Math.Max(
                    height,
                    MeasurePrintTextHeight(
                        Convert.ToString(
                            columns[index].Header)
                        ?? string.Empty,
                        columnWidths[index],
                        ResolveConfiguredPrintFontSize(
                            columns[index],
                            definition),
                        bold: true));
        }

        return height;
    }

    private double EstimatePrintRowHeight(
        IReadOnlyList<DataGridColumn> columns,
        IReadOnlyList<double> columnWidths,
        object row,
        MesReportDefinition? definition)
    {
        var height =
            16d;

        for (var index = 0;
             index < columns.Count;
             index++)
        {
            var column =
                columns[index];

            var text =
                GetConfiguredPrintableCellText(
                    column,
                    row);

            height =
                Math.Max(
                    height,
                    MeasurePrintTextHeight(
                        text,
                        columnWidths[index],
                        ResolveConfiguredPrintFontSize(
                            column,
                            definition),
                        bold: false));
        }

        return height;
    }

    private static double MeasurePrintTextHeight(
        string text,
        double columnWidth,
        double fontSize,
        bool bold)
    {
        var typeface =
            new Typeface(
                new FontFamily(
                    "Segoe UI"),
                FontStyles.Normal,
                bold
                    ? FontWeights.Bold
                    : FontWeights.Normal,
                FontStretches.Normal);

        var formatted =
            new FormattedText(
                string.IsNullOrEmpty(
                    text)
                    ? " "
                    : text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                Math.Max(
                    5d,
                    fontSize),
                Brushes.Black,
                1d)
            {
                MaxTextWidth =
                    Math.Max(
                        12d,
                        columnWidth - 8d)
            };

        return Math.Ceiling(
                   formatted.Height)
               + 8d;
    }

    private Table CreateConfiguredPrintTable(
        FlowDocument document,
        DataGrid grid,
        IReadOnlyList<DataGridColumn> columns,
        IReadOnlyList<object> rows,
        MesReportDefinition? definition,
        bool keepTogether)
    {
        var table =
            new Table
            {
                CellSpacing =
                    0};

        var columnWidths =
            CalculateConfiguredPrintColumnWidths(
                document,
                columns);

        foreach (var width in
                 columnWidths)
        {
            table.Columns.Add(
                new TableColumn
                {
                    Width =
                        new GridLength(
                            width)
                });
        }

        var rowGroup =
            new TableRowGroup();

        table.RowGroups.Add(
            rowGroup);

        var header =
            new TableRow
            {
                Background =
                    Brushes.Gainsboro
            };

        foreach (var column in
                 columns)
        {
            header.Cells.Add(
                CreateConfiguredPrintCell(
                    Convert.ToString(
                        column.Header)
                    ?? string.Empty,
                    column,
                    definition,
                    forceBold: true));
        }

        rowGroup.Rows.Add(
            header);

        foreach (var row in
                 rows)
        {
            var tableRow =
                new TableRow();

            ApplyPrintableStateRowColor(
                grid,
                row,
                tableRow);

            foreach (var column in
                     columns)
            {
                tableRow.Cells.Add(
                    CreateConfiguredPrintCell(
                        GetConfiguredPrintableCellText(
                            column,
                            row),
                        column,
                        definition,
                        forceBold: false));
            }

            rowGroup.Rows.Add(
                tableRow);
        }

        return table;
    }

    private string GetConfiguredPrintableCellText(
        DataGridColumn column,
        object row)
    {
        if (column
                is DataGridTextColumn textColumn
            && textColumn.Binding
                is Binding binding)
        {
            var property =
                binding.Path?.Path
                ?? string.Empty;

            property =
                property
                    .Trim()
                    .TrimStart('[')
                    .TrimEnd(']');

            var value =
                ReadPrintableValue(
                    row,
                    property);

            if (ShouldHideCurrentPlachtaZeroValue(
                    value))
            {
                return string.Empty;
            }

            return FormatPrintValue(
                value,
                binding.StringFormat);
        }

        return GetPrintableCellText(
            column,
            row);
    }

    private double ResolveConfiguredPrintFontSize(
        DataGridColumn column,
        MesReportDefinition? definition)
    {
        var global =
            _mes06ReportLayoutDocument.Global;

        if (definition is null)
        {
            return global.PrintFontSize;
        }

        var layout =
            FindColumnLayout(
                definition,
                ExtractGridColumnProperty(
                    column));

        return layout is not null
               && layout.FontSize > 0d
            ? Math.Max(
                5d,
                layout.FontSize * 0.68d)
            : global.PrintFontSize;
    }

    private TableCell CreateConfiguredPrintCell(
        string text,
        DataGridColumn column,
        MesReportDefinition? definition,
        bool forceBold)
    {
        Mes06ColumnLayoutSettings? layout =
            null;

        if (definition is not null)
        {
            layout =
                FindColumnLayout(
                    definition,
                    ExtractGridColumnProperty(
                        column));
        }

        var paragraph =
            new Paragraph
            {
                Margin =
                    new Thickness(
                        2,
                        1,
                        2,
                        1),
                FontSize =
                    ResolveConfiguredPrintFontSize(
                        column,
                        definition),
                FontWeight =
                    forceBold
                    || layout?.Bold == true
                        ? FontWeights.Bold
                        : FontWeights.Normal,
                TextAlignment =
                    ParseTextAlignment(
                        string.IsNullOrWhiteSpace(
                            layout?.Alignment)
                            ? _mes06ReportLayoutDocument.Global.DefaultAlignment
                            : layout.Alignment)
            };

        if (layout?.Underline == true)
        {
            paragraph.TextDecorations =
                TextDecorations.Underline;
        }

        paragraph.Inlines.Add(
            text);

        return new TableCell(
            paragraph)
        {
            BorderBrush =
                Brushes.LightGray,
            BorderThickness =
                new Thickness(
                    0.4),
            Padding =
                new Thickness(
                    1)
        };
    }
}
