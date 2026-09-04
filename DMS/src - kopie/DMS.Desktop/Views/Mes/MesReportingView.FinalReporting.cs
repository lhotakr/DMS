using ClosedXML.Excel;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Definitions;
using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private const string Mes06OeeReportCode = "OEE_REPORT";
    private const string Mes06ProcessValuesReportCode = "PROCESS_VALUES";

    private bool _mes06OeeReportActive;
    private bool _mes06ProcessValuesActive;
    private readonly List<Mes06ProcessStateItem> _mes06ProcessStateItems = new();

    private sealed class Mes06ProcessStateItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public string Name { get; init; } = string.Empty;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private IReadOnlyList<MesReportDefinition> EnsureFinalReportingDefinitions(
        IReadOnlyList<MesReportDefinition> definitions)
    {
        var result = definitions.ToList();
        var template = result.FirstOrDefault(definition =>
            string.Equals(definition.DataSource, "States", StringComparison.OrdinalIgnoreCase))
            ?? result.FirstOrDefault();

        if (template is null)
        {
            return result;
        }

        if (!result.Any(IsOeeReport))
        {
            var oee = CloneFinalReportDefinition(
                template,
                Mes06OeeReportCode,
                "OEE report",
                "MES06.Report.OEE.Name",
                "OEE per work center and personnel with availability, performance and quality losses.",
                "MES06.Report.OEE.Description");
            if (oee is not null) result.Add(oee);
        }

        if (!result.Any(IsProcessValuesReport))
        {
            var process = CloneFinalReportDefinition(
                template,
                Mes06ProcessValuesReportCode,
                "Process values",
                "MES06.Report.ProcessValues.Name",
                "FASTEC process-message timeline with order, article and state selection.",
                "MES06.Report.ProcessValues.Description");
            if (process is not null) result.Add(process);
        }

        return result;
    }

    private MesReportDefinition? CloneFinalReportDefinition(
        MesReportDefinition template,
        string code,
        string name,
        string nameKey,
        string description,
        string descriptionKey)
    {
        try
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(template)) as JsonObject;
            if (node is null) return null;
            node["Code"] = code;
            node["Name"] = name;
            node["NameKey"] = nameKey;
            node["Description"] = description;
            node["DescriptionKey"] = descriptionKey;
            node["DataSource"] = "States";
            node["Chart"] = null;
            node["MaxRows"] = 50000;
            return node.Deserialize<MesReportDefinition>();
        }
        catch (Exception ex)
        {
            _logger.Error($"MES06 final report definition could not be created. Code={code}", ex);
            return null;
        }
    }

    private static bool IsOeeReport(MesReportDefinition definition) =>
        string.Equals(definition.Code, Mes06OeeReportCode, StringComparison.OrdinalIgnoreCase);

    private static bool IsProcessValuesReport(MesReportDefinition definition) =>
        string.Equals(definition.Code, Mes06ProcessValuesReportCode, StringComparison.OrdinalIgnoreCase);

    private void ApplyFinalReportingLocalization()
    {
        if (ExpProcessStates is null) return;
        ExpProcessStates.Header = T("MES06.ProcessValues.Filter.Header", "Process values");
        LblProcessStates.Text = T("MES06.ProcessValues.Filter.States", "States");
        BtnSelectAllProcessStates.Content = T("MES06.ProcessValues.Filter.All", "All");
        BtnClearProcessStates.Content = T("MES06.ProcessValues.Filter.None", "None");
        UpdateProcessStateSelectionSummary();
    }

    private void UpdateFinalReportingFilterMode(MesReportDefinition? definition)
    {
        if (ExpProcessStates is null) return;
        ExpProcessStates.Visibility =
            definition is not null && IsProcessValuesReport(definition)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpdateProcessValueStateChoices(IReadOnlyList<object> rows)
    {
        var previous = _mes06ProcessStateItems.ToDictionary(
            item => item.Name,
            item => item.IsSelected,
            StringComparer.CurrentCultureIgnoreCase);

        var names = rows
            .OfType<Mes06ProcessValueRecord>()
            .Select(row => string.IsNullOrWhiteSpace(row.StateName)
                ? T("MES06.ProcessValues.EmptyState", "(unnamed)")
                : row.StateName.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _mes06ProcessStateItems.Clear();
        foreach (var name in names)
        {
            var item = new Mes06ProcessStateItem
            {
                Name = name,
                IsSelected = !previous.TryGetValue(name, out var selected) || selected
            };
            item.PropertyChanged += ProcessStateItem_PropertyChanged;
            _mes06ProcessStateItems.Add(item);
        }

        ProcessStateItems.ItemsSource = null;
        ProcessStateItems.ItemsSource = _mes06ProcessStateItems;
        UpdateProcessStateSelectionSummary();
    }

    private void ProcessStateItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Mes06ProcessStateItem.IsSelected))
        {
            UpdateProcessStateSelectionSummary();
        }
    }

    private IReadOnlyList<string> GetSelectedProcessValueStates() =>
        _mes06ProcessStateItems
            .Where(item => item.IsSelected)
            .Select(item => item.Name)
            .ToList();

    private IReadOnlyList<object> ApplyProcessValueStateFilter(IReadOnlyList<object> rows)
    {
        if (_mes06ProcessStateItems.Count == 0)
        {
            return rows;
        }

        var selected = GetSelectedProcessValueStates().ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return rows
            .OfType<Mes06ProcessValueRecord>()
            .Where(row => selected.Contains(
                string.IsNullOrWhiteSpace(row.StateName)
                    ? T("MES06.ProcessValues.EmptyState", "(unnamed)")
                    : row.StateName.Trim()))
            .Cast<object>()
            .ToList();
    }

    private void BtnSelectAllProcessStates_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _mes06ProcessStateItems) item.IsSelected = true;
        UpdateProcessStateSelectionSummary();
    }

    private void BtnClearProcessStates_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _mes06ProcessStateItems) item.IsSelected = false;
        UpdateProcessStateSelectionSummary();
    }

    private void UpdateProcessStateSelectionSummary()
    {
        if (TxtProcessStateSelectionSummary is null) return;
        var selected = _mes06ProcessStateItems.Count(item => item.IsSelected);
        var total = _mes06ProcessStateItems.Count;
        TxtProcessStateSelectionSummary.Text = total == 0
            ? T("MES06.ProcessValues.Filter.AllDefault", "All states")
            : selected == total
                ? string.Format(T("MES06.ProcessValues.Filter.AllCount", "All states ({0})"), total)
                : string.Format(T("MES06.ProcessValues.Filter.SelectedCount", "Selected: {0}/{1}"), selected, total);
    }

    private DataGridTextColumn FinalTextColumn(string headerKey, string fallback, string path, double width)
    {
        return new DataGridTextColumn
        {
            Header = T(headerKey, fallback),
            Binding = new Binding(path) { Mode = BindingMode.OneWay },
            SortMemberPath = path,
            Width = new DataGridLength(width)
        };
    }

    private void BuildOeeReportPresentation(MesReportDefinition definition)
    {
        _mes06OeeReportActive = true;
        CounterSummaryBorder.Visibility = Visibility.Collapsed;
        GridCounterSummary.ItemsSource = null;
        Grid.SetRowSpan(ChartBorder, 1);
        ChartBorder.Height = 360d;
        ChartBorder.MinHeight = 320d;
        ChartBorder.Visibility = Visibility.Visible;
        GridReport.Visibility = Visibility.Visible;

        GridReport.Columns.Clear();
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Workcenter", "Work center", "WorkcenterCode", 85));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Shift", "Shift", "ShiftName", 105));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Personnel", "Personnel", "Personnel", 300));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Failure", "Failure", "FailureText", 82));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Available", "Available", "AvailableText", 82));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.PlannedShutdown", "Planned shutdown", "PlannedShutdownText", 105));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Total", "Total amount", "TotalAmountText", 80));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Bad", "Bad units", "BadUnitsText", 72));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Rework", "Rework", "ReworkUnitsText", 72));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Good", "Good units", "GoodUnitsText", 76));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.ActualPerf", "Ø Actual perf.", "ActualPerformanceText", 90));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.PlannedPerf", "Ø Planned perf.", "PlannedPerformanceText", 95));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Equipment", "Equipment util.", "EquipmentUtilizationText", 95));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Availability", "Availability (OEE)", "AvailabilityOeeText", 105));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Performance", "Performance (OEE)", "PerformanceOeeText", 108));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Quality", "Quality (OEE)", "QualityOeeText", 92));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.Benefit", "Performance benefit", "PerformanceBenefitText", 105));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.OEE", "OEE", "OeeText", 72));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.NEE", "NEE", "NeeText", 72));
        GridReport.Columns.Add(FinalTextColumn("MES06.OEE.TEEP", "TEEP", "TeepText", 72));
        GridReport.ItemsSource = _currentRows;

        TxtChartTitle.Text = T("MES06.Report.OEE.ChartTitle", "OEE losses");
        var rows = _currentRows.OfType<Mes06OeeReportRecord>().ToList();
        var width = Math.Max(1000d, rows.Count * 105d + 110d);
        ChartHost.Content = new ScrollViewer
        {
            Content = new Mes06OeeChartElement(
                rows,
                width,
                false,
                GetOeeLegendLabels()),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White
        };
    }

    private void ResetOeeReportPresentation()
    {
        if (!_mes06OeeReportActive) return;
        _mes06OeeReportActive = false;
        ChartBorder.Height = 220d;
        ChartBorder.MinHeight = 0d;
        Grid.SetRowSpan(ChartBorder, 1);
        ChartHost.Content = null;
        GridReport.Visibility = Visibility.Visible;
    }

    private sealed class Mes06OeeChartElement : FrameworkElement
    {
        private readonly IReadOnlyList<Mes06OeeReportRecord> _rows;
        private readonly bool _compact;
        private readonly IReadOnlyList<string> _legendLabels;

        public Mes06OeeChartElement(IReadOnlyList<Mes06OeeReportRecord> rows, double width, bool compact, IReadOnlyList<string> legendLabels)
        {
            _rows = rows;
            _compact = compact;
            _legendLabels = legendLabels;
            Width = width;
            Height = compact ? 315d : 350d;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0d, 0d, ActualWidth, ActualHeight));
            if (_rows.Count == 0) return;

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var left = 48d;
            var labelTop = 8d;
            var plotTop = _compact ? 64d : 78d;
            var plotBottom = _compact ? 235d : 260d;
            var right = ActualWidth - 12d;
            var maxStack = Math.Max(100d, _rows.Max(row => row.Oee + row.AvailabilityLoss + row.PerformanceLoss + row.QualityLoss));
            maxStack = Math.Max(110d, Math.Ceiling(maxStack / 10d) * 10d);
            double Y(double value) => plotBottom - Math.Clamp(value / maxStack, 0d, 1d) * (plotBottom - plotTop);

            var gridPen = new Pen(Brushes.LightGray, 0.6d);
            for (double p = 0d; p <= maxStack; p += 20d)
            {
                var y = Y(p);
                dc.DrawLine(gridPen, new Point(left, y), new Point(right, y));
                var ft = new FormattedText($"{p:0}%", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 7.2d, Brushes.Black, dpi);
                dc.DrawText(ft, new Point(left - ft.Width - 5d, y - ft.Height / 2d));
            }

            var slot = (right - left) / Math.Max(1, _rows.Count);
            var bar = Math.Min(46d, slot * 0.62d);
            var labelRowStep = _compact ? 15d : 17d;

            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                var x = left + index * slot + (slot - bar) / 2d;

                // Stagger chart labels into three rows:
                // 1st bar -> row 1, 2nd -> row 2, 3rd -> row 3,
                // 4th -> row 1, etc. Labels on the same row are therefore
                // three slots apart and can use a substantially wider area.
                var labelRow = index % 3;
                var labelY = labelTop + labelRow * labelRowStep;
                var labelWidth = Math.Min(
                    250d,
                    Math.Max(
                        78d,
                        slot * 2.72d));

                DrawOeeBarLabel(
                    dc,
                    row,
                    x + bar / 2d,
                    labelY,
                    labelWidth,
                    dpi);

                var current = 0d;
                DrawStack(dc, x, bar, ref current, row.Oee, Color.FromRgb(35,230,85), Y, dpi);
                DrawStack(dc, x, bar, ref current, row.AvailabilityLoss, Color.FromRgb(255,55,50), Y, dpi);
                DrawStack(dc, x, bar, ref current, row.PerformanceLoss, Color.FromRgb(255,245,175), Y, dpi);
                DrawStack(dc, x, bar, ref current, row.QualityLoss, Color.FromRgb(55,55,235), Y, dpi);
            }

            DrawOeeLegendBottom(dc, left, plotBottom + 18d, right - left, dpi, _legendLabels);
        }

        private void DrawOeeBarLabel(DrawingContext dc, Mes06OeeReportRecord row, double centerX, double y, double maxWidth, double dpi)
        {
            var label = string.IsNullOrWhiteSpace(row.ShiftName)
                ? row.WorkcenterCode
                : $"{row.WorkcenterCode} · {row.ShiftName}";

            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                _compact ? 5.8d : 6.6d, Brushes.Black, dpi)
            {
                MaxTextWidth = maxWidth,
                MaxTextHeight = _compact ? 12d : 14d,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(ft, new Point(centerX - maxWidth / 2d, y));
        }

        private static void DrawStack(DrawingContext dc, double x, double width, ref double current, double value, Color color, Func<double,double> y, double dpi)
        {
            if (value <= 0d) return;
            var y0 = y(current);
            current += value;
            var y1 = y(current);
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(x, y1, width, Math.Max(0.8d, y0-y1)));
            if (value >= 3d)
            {
                var ft = new FormattedText($"{value:0.00}%", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 6.2d, Brushes.Black, dpi);
                dc.DrawText(ft, new Point(x + (width-ft.Width)/2d, y1 + (y0-y1-ft.Height)/2d));
            }
        }

        private static void DrawOeeLegendBottom(DrawingContext dc, double left, double y, double width, double dpi, IReadOnlyList<string> labels)
        {
            var items = new[]
            {
                (labels.Count > 0 ? labels[0] : "OEE", Color.FromRgb(35,230,85)),
                (labels.Count > 1 ? labels[1] : "Availability loss", Color.FromRgb(255,55,50)),
                (labels.Count > 2 ? labels[2] : "Performance loss", Color.FromRgb(255,245,175)),
                (labels.Count > 3 ? labels[3] : "Quality loss", Color.FromRgb(55,55,235))
            };

            var itemWidth = width / items.Length;
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var x = left + index * itemWidth;
                dc.DrawRectangle(new SolidColorBrush(item.Item2), new Pen(Brushes.Gray,0.4d), new Rect(x, y+1d, 11d, 11d));
                var ft = new FormattedText(item.Item1, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 6.8d, Brushes.Black, dpi)
                {
                    MaxTextWidth = Math.Max(40d, itemWidth - 19d),
                    Trimming = TextTrimming.CharacterEllipsis
                };
                dc.DrawText(ft, new Point(x+16d, y));
            }
        }
    }

    private void BuildProcessValuesPresentation(MesReportDefinition definition)
    {
        _mes06ProcessValuesActive = true;
        CounterSummaryBorder.Visibility = Visibility.Collapsed;
        GridCounterSummary.ItemsSource = null;
        Grid.SetRowSpan(ChartBorder, 1);
        ChartBorder.Height = 330d;
        ChartBorder.MinHeight = 280d;
        ChartBorder.Visibility = Visibility.Visible;
        GridReport.Visibility = Visibility.Visible;
        GridReport.Columns.Clear();
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Level", "Level", "Level", 62));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Workcenter", "Work center", "WorkcenterCode", 82));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.State", "State", "StateName", 190));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.From", "From", "Starttime", 125));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.To", "To", "Endtime", 125));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Duration", "Duration", "DurationText", 90));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Order", "Order", "OrderCode", 90));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Article", "Article", "ProductCode", 115));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Operation", "Operation", "OperationCode", 82));
        GridReport.Columns.Add(FinalTextColumn("MES06.ProcessValues.Shift", "Shift", "ShiftName", 100));
        GridReport.ItemsSource = _currentRows;

        BuildProcessValuesDurationSummary();

        TxtChartTitle.Text = T("MES06.Report.ProcessValues.ChartTitle", "Process values – timeline");
        var element = CreateProcessValuesTimelineElement(false, Math.Max(1120d, ChartHost.ActualWidth - 10d));
        ChartHost.Content = new ScrollViewer
        {
            Content = element,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White
        };
    }

    private void ResetProcessValuesPresentation()
    {
        if (!_mes06ProcessValuesActive) return;
        _mes06ProcessValuesActive = false;
        ChartBorder.Height = 220d;
        ChartBorder.MinHeight = 0d;
        Grid.SetRowSpan(ChartBorder,1);
        ChartHost.Content = null;
        GridReport.Visibility = Visibility.Visible;

        CounterSummaryBorder.Visibility =
            Visibility.Collapsed;

        GridCounterSummary.ItemsSource =
            null;

        GridCounterSummary.Columns.Clear();
    }

    private void BuildProcessValuesDurationSummary()
    {
        var rows =
            _currentRows
                .OfType<Mes06ProcessValueRecord>()
                .Where(row =>
                    row.Endtime > row.Starttime)
                .ToList();

        GridCounterSummary.Columns.Clear();

        if (rows.Count == 0)
        {
            GridCounterSummary.ItemsSource =
                null;

            CounterSummaryBorder.Visibility =
                Visibility.Collapsed;

            return;
        }

        var summary =
            rows
                .GroupBy(
                    row =>
                        new
                        {
                            row.WorkcenterCode,
                            row.StateName
                        })
                .Select(group =>
                {
                    // Merge overlapping/touching intervals first. This prevents
                    // duplicated process messages from inflating active time.
                    var intervals =
                        group
                            .Select(row =>
                                new
                                {
                                    From =
                                        row.Starttime,
                                    To =
                                        row.Endtime
                                })
                            .OrderBy(interval =>
                                interval.From)
                            .ThenBy(interval =>
                                interval.To)
                            .ToList();

                    var merged =
                        new List<(DateTime From, DateTime To)>();

                    foreach (var interval
                             in intervals)
                    {
                        if (merged.Count == 0)
                        {
                            merged.Add(
                                (
                                    interval.From,
                                    interval.To
                                ));

                            continue;
                        }

                        var last =
                            merged[^1];

                        if (interval.From <=
                            last.To)
                        {
                            if (interval.To >
                                last.To)
                            {
                                merged[^1] =
                                    (
                                        last.From,
                                        interval.To
                                    );
                            }

                            continue;
                        }

                        merged.Add(
                            (
                                interval.From,
                                interval.To
                            ));
                    }

                    var totalSeconds =
                        merged.Sum(interval =>
                            (
                                interval.To
                                - interval.From
                            ).TotalSeconds);

                    var occurrences =
                        merged.Count;

                    var averageSeconds =
                        occurrences > 0
                            ? totalSeconds
                              / occurrences
                            : 0d;

                    return new Mes06ProcessValueSummaryRow
                    {
                        WorkcenterCode =
                            group.Key.WorkcenterCode,
                        StateName =
                            group.Key.StateName,
                        Occurrences =
                            occurrences,
                        TotalDurationSeconds =
                            totalSeconds,
                        TotalDurationText =
                            FormatProcessValueDuration(
                                totalSeconds),
                        AverageDurationText =
                            FormatProcessValueDuration(
                                averageSeconds)
                    };
                })
                .OrderBy(row =>
                    row.WorkcenterCode,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(row =>
                    row.TotalDurationSeconds)
                .ThenBy(row =>
                    row.StateName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        AddProcessValueSummaryColumn(
            T(
                "MES06.ProcessValues.Summary.Workcenter",
                "Work center"),
            nameof(
                Mes06ProcessValueSummaryRow.WorkcenterCode),
            115d);

        AddProcessValueSummaryColumn(
            T(
                "MES06.ProcessValues.Summary.Process",
                "Process value"),
            nameof(
                Mes06ProcessValueSummaryRow.StateName),
            300d);

        AddProcessValueSummaryColumn(
            T(
                "MES06.ProcessValues.Summary.Count",
                "Occurrences"),
            nameof(
                Mes06ProcessValueSummaryRow.Occurrences),
            100d);

        AddProcessValueSummaryColumn(
            T(
                "MES06.ProcessValues.Summary.TotalDuration",
                "Total duration"),
            nameof(
                Mes06ProcessValueSummaryRow.TotalDurationText),
            135d);

        AddProcessValueSummaryColumn(
            T(
                "MES06.ProcessValues.Summary.AverageDuration",
                "Average duration"),
            nameof(
                Mes06ProcessValueSummaryRow.AverageDurationText),
            135d);

        GridCounterSummary.ItemsSource =
            summary;

        TxtCounterSummaryTitle.Text =
            T(
                "MES06.ProcessValues.Summary.Title",
                "Process value duration summary");

        CounterSummaryBorder.Visibility =
            summary.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void AddProcessValueSummaryColumn(
        string header,
        string property,
        double width)
    {
        GridCounterSummary.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    header,
                Binding =
                    new Binding(
                        property)
                    {
                        Mode =
                            BindingMode.OneWay
                    },
                Width =
                    new DataGridLength(
                        width)
            });
    }

    private static string FormatProcessValueDuration(
        double seconds)
    {
        if (double.IsNaN(
                seconds)
            || double.IsInfinity(
                seconds)
            || seconds < 0d)
        {
            seconds =
                0d;
        }

        var value =
            TimeSpan.FromSeconds(
                seconds);

        var days =
            (int)value.TotalDays;

        return days > 0
            ? $"{days}d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    private FrameworkElement CreateProcessValuesTimelineElement(bool compact, double requestedWidth)
    {
        var rows=_currentRows.OfType<Mes06ProcessValueRecord>().OrderBy(r=>r.WorkcenterCode).ThenBy(r=>r.StateName).ThenBy(r=>r.Starttime).ToList();
        var from=_mes06EffectiveFrom;
        var to=_mes06EffectiveTo>from?_mes06EffectiveTo:from.AddDays(1);
        var now=DateTime.Now;
        if (from <= now && to > now)
        {
            to=now;
        }
        var lanes=rows.GroupBy(r=>$"{r.WorkcenterCode}|{r.StateName}").Select(g=>new Mes06ProcessLane{Label=g.Key,Rows=g.ToList()}).ToList();
        var width=Math.Max(requestedWidth, 130d + Math.Max(1d,(to-from).TotalHours)*44d);
        return new Mes06ProcessTimelineElement(lanes,from,to,width,compact);
    }

    private sealed class Mes06ProcessLane
    {
        public string Label { get; init; }=string.Empty;
        public IReadOnlyList<Mes06ProcessValueRecord> Rows { get; init; }=Array.Empty<Mes06ProcessValueRecord>();
    }

    private sealed class Mes06ProcessValueSummaryRow
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string StateName { get; init; } = string.Empty;
        public int Occurrences { get; init; }
        public double TotalDurationSeconds { get; init; }
        public string TotalDurationText { get; init; } = string.Empty;
        public string AverageDurationText { get; init; } = string.Empty;
    }

    private sealed class Mes06ProcessTimelineElement : FrameworkElement
    {
        private readonly IReadOnlyList<Mes06ProcessLane> _lanes;
        private readonly DateTime _from;
        private readonly DateTime _to;
        private readonly bool _compact;
        public Mes06ProcessTimelineElement(IReadOnlyList<Mes06ProcessLane> lanes,DateTime from,DateTime to,double width,bool compact)
        {
            _lanes=lanes; _from=from; _to=to; _compact=compact; Width=width;
            var rowH=compact?10d:16d;
            Height=Math.Max(compact?180d:220d,28d+lanes.Count*rowH+38d);
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
            if(_to<=_from||_lanes.Count==0)return;
            var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var left=_compact?125d:175d; var right=ActualWidth-8d; var top=8d; var rowH=_compact?10d:16d; var bottom=top+_lanes.Count*rowH;
            double X(DateTime t)=>left+Math.Clamp((t-_from).TotalSeconds/(_to-_from).TotalSeconds,0d,1d)*(right-left);
            var tick=new DateTime(_from.Year,_from.Month,_from.Day,_from.Hour,_from.Minute<30?0:30,0);
            if(tick<_from)tick=tick.AddMinutes(30);
            while(tick<_to)
            {
                var x=X(tick); var pen=new Pen(tick.Minute==0?Brushes.Gray:Brushes.LightGray,tick.Minute==0?0.8:0.45);
                dc.DrawLine(pen,new Point(x,top),new Point(x,bottom));
                if(tick.Minute==0)
                {
                    var ft=new FormattedText(tick.ToString("dd.MM HH:mm"),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),_compact?5.8:7,Brushes.Black,dpi);
                    dc.DrawText(ft,new Point(x-ft.Width/2,bottom+5));
                }
                tick=tick.AddMinutes(30);
            }
            for(var i=0;i<_lanes.Count;i++)
            {
                var y=top+i*rowH;
                dc.DrawLine(new Pen(Brushes.Gainsboro,0.4),new Point(left,y),new Point(right,y));
                var ft=new FormattedText(_lanes[i].Label,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),_compact?5.8:7.2,Brushes.Black,dpi){MaxTextWidth=left-8,Trimming=TextTrimming.CharacterEllipsis};
                dc.DrawText(ft,new Point(3,y+Math.Max(0,(rowH-ft.Height)/2)));
                foreach(var r in _lanes[i].Rows)
                {
                    var x1=X(r.Starttime); var x2=X(r.Endtime);
                    dc.DrawRectangle(Brushes.LimeGreen,null,new Rect(x1,y+1,Math.Max(1,x2-x1),Math.Max(1,rowH-2)));
                }
            }
            dc.DrawRectangle(null,new Pen(Brushes.Gray,0.7),new Rect(left,top,right-left,bottom-top));
        }
    }

    private IReadOnlyList<string> GetOeeLegendLabels() =>
        new[]
        {
            T("MES06.OEE.Legend.OEE", "OEE"),
            T("MES06.OEE.Legend.AvailabilityLoss", "Availability loss"),
            T("MES06.OEE.Legend.PerformanceLoss", "Performance loss"),
            T("MES06.OEE.Legend.QualityLoss", "Quality loss")
        };

    private void AppendOeeChartToDocument(System.Windows.Documents.FlowDocument document)
    {
        var rows=_currentRows.OfType<Mes06OeeReportRecord>().ToList();
        if(rows.Count==0)return;
        var width=Math.Max(700d,document.PageWidth-document.PagePadding.Left-document.PagePadding.Right);
        var element=new Mes06OeeChartElement(rows,width,true,GetOeeLegendLabels());
        AppendFinalReportingElementToDocument(document,element,width,285d);
    }

    private void AppendProcessValuesTimelineToDocument(System.Windows.Documents.FlowDocument document)
    {
        if(_currentRows.Count==0)return;
        var width=Math.Max(700d,document.PageWidth-document.PagePadding.Left-document.PagePadding.Right);
        var element=CreateProcessValuesTimelineElement(true,width);
        AppendFinalReportingElementToDocument(document,element,width,390d);
    }

    private static void AppendFinalReportingElementToDocument(System.Windows.Documents.FlowDocument document,FrameworkElement element,double width,double maxHeight)
    {
        element.Measure(new Size(element.Width,element.Height));
        element.Arrange(new Rect(0,0,element.Width,element.Height));
        element.UpdateLayout();
        const double dpi=144d; var scale=dpi/96d;
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(element.Width*scale)),Math.Max(1,(int)Math.Ceiling(element.Height*scale)),dpi,dpi,PixelFormats.Pbgra32);
        bitmap.Render(element); if(bitmap.CanFreeze)bitmap.Freeze();
        var displayScale=Math.Min(1d,maxHeight/Math.Max(1d,element.Height));
        document.Blocks.Add(new System.Windows.Documents.BlockUIContainer(new Image{Source=bitmap,Width=width*displayScale,Height=element.Height*displayScale,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left}){Margin=new Thickness(0,0,0,10)});
    }

    private void ExportFinalReportingVisibleGridExcel(MesReportDefinition definition)
    {
        var rows=GridReport.Items.Cast<object>().Where(item=>item!=System.Windows.Data.CollectionView.NewItemPlaceholder).ToList();
        if(rows.Count==0)
        {
            DmsMessage.Show(T("MES06.Status.NoData","There is no report data to export."),"MES06",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        var dialog=new SaveFileDialog{Filter="Excel workbook (*.xlsx)|*.xlsx",FileName=$"MES06_{definition.Code}_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"};
        if(dialog.ShowDialog()!=true)return;
        try
        {
            using var workbook=new XLWorkbook(); var ws=workbook.Worksheets.Add("MES Report");
            var columns=GridReport.Columns.Where(c=>c.Visibility==Visibility.Visible).OrderBy(c=>c.DisplayIndex).ToList();
            for(var c=0;c<columns.Count;c++) ws.Cell(1,c+1).Value=Convert.ToString(columns[c].Header)??string.Empty;
            for(var r=0;r<rows.Count;r++)
            {
                for(var c=0;c<columns.Count;c++)
                {
                    var property=columns[c].SortMemberPath;
                    if(string.IsNullOrWhiteSpace(property) && columns[c] is DataGridBoundColumn bound && bound.Binding is Binding binding) property=binding.Path?.Path??string.Empty;
                    SetExcelCellValue(ws.Cell(r+2,c+1),ReadProperty(rows[r],property));
                }
            }
            ws.ColumnsUsed().AdjustToContents(); workbook.SaveAs(dialog.FileName);
            _logger.AdminAction("MES06","ExportMesReportExcel",_user,$"Report={definition.Code}; Rows={rows.Count}; File={dialog.FileName}");
            OfferOpenExportedFile(dialog.FileName);
        }
        catch(Exception ex)
        {
            _logger.Error("MES06 final report Excel export failed.",ex);
            DmsMessage.Show(ex.Message,"MES06",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }
}
