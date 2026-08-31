using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Definitions;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private const string Mes06ProductionGraphReportCode =
        "PRODUCTION_GRAPH";

    private const int Mes06ProductionGraphPointStepMinutes =
        5;

    private bool _mes06ProductionGraphActive;

    private sealed class Mes06ProductionRateInterval
    {
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public double Rate { get; init; }
    }

    private sealed class Mes06ProductionRatePoint
    {
        public DateTime Time { get; init; }
        public double Rate { get; init; }
    }

    private sealed class Mes06ProductionStateSegment
    {
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public string StateName { get; init; } = string.Empty;
        public Color Color { get; init; }
    }

    private sealed class Mes06ProductionLegendItem
    {
        public string Label { get; init; } = string.Empty;
        public Color Color { get; init; }
    }

    private sealed class Mes06ProductionGraphElement
        : FrameworkElement
    {
        private readonly IReadOnlyList<Mes06ProductionRatePoint> _points;
        private readonly IReadOnlyList<Mes06ProductionRatePoint> _plannedPoints;
        private readonly IReadOnlyList<Mes06ProductionStateSegment> _states;
        private readonly IReadOnlyList<Mes06ProductionLegendItem> _legend;
        private readonly DateTime _from;
        private readonly DateTime _to;
        private readonly string _yAxisTitle;
        private readonly string _rateLegendText;
        private readonly string _plannedRateLegendText;
        private readonly string _stateLegendTitle;
        private readonly bool _compact;

        private readonly Color _rateColor =
            Color.FromRgb(
                210,
                35,
                135);

        private readonly Color _plannedRateColor =
            Color.FromRgb(
                70,
                70,
                70);

        public Mes06ProductionGraphElement(
            IReadOnlyList<Mes06ProductionRatePoint> points,
            IReadOnlyList<Mes06ProductionRatePoint> plannedPoints,
            IReadOnlyList<Mes06ProductionStateSegment> states,
            IReadOnlyList<Mes06ProductionLegendItem> legend,
            DateTime from,
            DateTime to,
            string yAxisTitle,
            string rateLegendText,
            string plannedRateLegendText,
            string stateLegendTitle,
            double width,
            bool compact)
        {
            _points = points;
            _plannedPoints = plannedPoints;
            _states = states;
            _legend = legend;
            _from = from;
            _to = to;
            _yAxisTitle = yAxisTitle;
            _rateLegendText = rateLegendText;
            _plannedRateLegendText = plannedRateLegendText;
            _stateLegendTitle = stateLegendTitle;
            _compact = compact;

            Width = width;
            Height =
                compact
                    ? 455d
                    : 520d;
        }

        protected override void OnRender(
            DrawingContext dc)
        {
            base.OnRender(
                dc);

            dc.DrawRectangle(
                Brushes.White,
                null,
                new Rect(
                    0d,
                    0d,
                    ActualWidth,
                    ActualHeight));

            if (_to <= _from)
            {
                return;
            }

            var pixelsPerDip =
                VisualTreeHelper.GetDpi(
                        this)
                    .PixelsPerDip;

            var left =
                _compact
                    ? 58d
                    : 72d;

            var right =
                Math.Max(
                    left + 160d,
                    ActualWidth - 12d);

            var top =
                _compact
                    ? 14d
                    : 18d;

            var plotBottom =
                _compact
                    ? 335d
                    : 390d;

            var plotHeight =
                plotBottom - top;

            var plotWidth =
                right - left;

            var totalSeconds =
                (
                    _to
                    - _from
                ).TotalSeconds;

            double ToX(
                DateTime value)
            {
                var ratio =
                    (
                        value
                        - _from
                    ).TotalSeconds
                    / totalSeconds;

                return left
                       + Math.Clamp(
                           ratio,
                           0d,
                           1d)
                       * plotWidth;
            }

            var maxRate =
                ResolveAxisMaximum();

            double ToY(
                double rate)
            {
                var ratio =
                    Math.Clamp(
                        rate / maxRate,
                        0d,
                        1d);

                return plotBottom
                       - ratio
                       * plotHeight;
            }

            DrawStateBackground(
                dc,
                top,
                plotBottom,
                ToX);

            DrawHorizontalGrid(
                dc,
                left,
                right,
                top,
                plotBottom,
                maxRate,
                ToY,
                pixelsPerDip);

            DrawHalfHourColumns(
                dc,
                top,
                plotBottom,
                ToX,
                pixelsPerDip);

            DrawPlannedRateLine(
                dc,
                ToX,
                ToY);

            DrawRateLine(
                dc,
                ToX,
                ToY);

            DrawYAxisTitle(
                dc,
                top,
                plotBottom,
                pixelsPerDip);

            DrawLegend(
                dc,
                left,
                plotBottom + 46d,
                right - left,
                pixelsPerDip);
        }

        private double ResolveAxisMaximum()
        {
            var maximum =
                Math.Max(
                    _points.Count == 0
                        ? 0d
                        : _points.Max(point =>
                            point.Rate),
                    _plannedPoints.Count == 0
                        ? 0d
                        : _plannedPoints.Max(point =>
                            point.Rate));

            if (maximum <= 0d)
            {
                return 50d;
            }

            var padded =
                maximum * 1.15d;

            double step;

            if (padded <= 25d)
            {
                step = 5d;
            }
            else if (padded <= 60d)
            {
                step = 10d;
            }
            else if (padded <= 150d)
            {
                step = 25d;
            }
            else
            {
                step = 50d;
            }

            return Math.Max(
                step,
                Math.Ceiling(
                    padded / step)
                * step);
        }

        private void DrawStateBackground(
            DrawingContext dc,
            double top,
            double bottom,
            Func<DateTime, double> toX)
        {
            foreach (var state
                     in _states)
            {
                var from =
                    state.From < _from
                        ? _from
                        : state.From;

                var to =
                    state.To > _to
                        ? _to
                        : state.To;

                if (to <= from)
                {
                    continue;
                }

                var x1 =
                    toX(
                        from);

                var x2 =
                    toX(
                        to);

                var background =
                    new SolidColorBrush(
                        Color.FromArgb(
                            _compact
                                ? (byte)78
                                : (byte)92,
                            state.Color.R,
                            state.Color.G,
                            state.Color.B));

                dc.DrawRectangle(
                    background,
                    null,
                    new Rect(
                        x1,
                        top,
                        Math.Max(
                            0.8d,
                            x2 - x1),
                        bottom - top));
            }
        }

        private void DrawHorizontalGrid(
            DrawingContext dc,
            double left,
            double right,
            double top,
            double bottom,
            double maxRate,
            Func<double, double> toY,
            double pixelsPerDip)
        {
            var gridPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            210,
                            210,
                            210)),
                    0.55d);

            var axisPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            100,
                            100,
                            100)),
                    0.8d);

            var divisions =
                5;

            for (var index = 0;
                 index <= divisions;
                 index++)
            {
                var rate =
                    maxRate
                    * index
                    / divisions;

                var y =
                    toY(
                        rate);

                dc.DrawLine(
                    index == 0
                        ? axisPen
                        : gridPen,
                    new Point(
                        left,
                        y),
                    new Point(
                        right,
                        y));

                var label =
                    new FormattedText(
                        rate.ToString(
                            "0",
                            CultureInfo.CurrentCulture),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            "Segoe UI"),
                        _compact
                            ? 7d
                            : 8.5d,
                        Brushes.Black,
                        pixelsPerDip);

                dc.DrawText(
                    label,
                    new Point(
                        left
                        - label.Width
                        - 6d,
                        y
                        - label.Height / 2d));
            }

            dc.DrawLine(
                axisPen,
                new Point(
                    left,
                    top),
                new Point(
                    left,
                    bottom));
        }

        private void DrawHalfHourColumns(
            DrawingContext dc,
            double top,
            double bottom,
            Func<DateTime, double> toX,
            double pixelsPerDip)
        {
            var halfHourPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(
                            185,
                            170,
                            170,
                            170)),
                    0.55d);

            var fullHourPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromArgb(
                            220,
                            100,
                            100,
                            100)),
                    0.95d);

            var first =
                CeilToHalfHour(
                    _from);

            var tick =
                first;

            var previousX =
                toX(
                    _from);

            var estimatedHalfHourWidth =
                Math.Abs(
                    toX(
                        _from.AddMinutes(
                            30))
                    - previousX);

            var labelEveryHalfHour =
                !_compact
                && estimatedHalfHourWidth >= 40d;

            while (tick < _to)
            {
                var x =
                    toX(
                        tick);

                dc.DrawLine(
                    tick.Minute == 0
                        ? fullHourPen
                        : halfHourPen,
                    new Point(
                        x,
                        top),
                    new Point(
                        x,
                        bottom));

                if (labelEveryHalfHour
                    || tick.Minute == 0)
                {
                    var label =
                        new FormattedText(
                            tick.ToString(
                                "HH:mm",
                                CultureInfo.CurrentCulture),
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(
                                "Segoe UI"),
                            _compact
                                ? 6.8d
                                : 8d,
                            Brushes.Black,
                            pixelsPerDip);

                    dc.DrawText(
                        label,
                        new Point(
                            x
                            - label.Width / 2d,
                            bottom + 7d));
                }

                tick =
                    tick.AddMinutes(
                        30);
            }

            var fromLabel =
                new FormattedText(
                    _from.ToString(
                        "dd.MM HH:mm",
                        CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        "Segoe UI"),
                    _compact
                        ? 6.5d
                        : 7.3d,
                    Brushes.DimGray,
                    pixelsPerDip);

            dc.DrawText(
                fromLabel,
                new Point(
                    toX(
                        _from),
                    bottom + 27d));

            var toLabel =
                new FormattedText(
                    _to.ToString(
                        "dd.MM HH:mm",
                        CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        "Segoe UI"),
                    _compact
                        ? 6.5d
                        : 7.3d,
                    Brushes.DimGray,
                    pixelsPerDip);

            dc.DrawText(
                toLabel,
                new Point(
                    toX(
                        _to)
                    - toLabel.Width,
                    bottom + 27d));
        }

        private void DrawPlannedRateLine(
            DrawingContext dc,
            Func<DateTime, double> toX,
            Func<double, double> toY)
        {
            if (_plannedPoints.Count == 0)
            {
                return;
            }

            var linePen =
                new Pen(
                    new SolidColorBrush(
                        _plannedRateColor),
                    _compact
                        ? 1.35d
                        : 1.7d)
                {
                    DashStyle =
                        DashStyles.Dash
                };

            var geometry =
                new StreamGeometry();

            using (var context =
                   geometry.Open())
            {
                var first =
                    _plannedPoints[0];

                context.BeginFigure(
                    new Point(
                        toX(
                            first.Time),
                        toY(
                            first.Rate)),
                    false,
                    false);

                foreach (var point
                         in _plannedPoints.Skip(
                             1))
                {
                    context.LineTo(
                        new Point(
                            toX(
                                point.Time),
                            toY(
                                point.Rate)),
                        true,
                        false);
                }
            }

            geometry.Freeze();

            dc.DrawGeometry(
                null,
                linePen,
                geometry);
        }

        private void DrawRateLine(
            DrawingContext dc,
            Func<DateTime, double> toX,
            Func<double, double> toY)
        {
            if (_points.Count == 0)
            {
                return;
            }

            var linePen =
                new Pen(
                    new SolidColorBrush(
                        _rateColor),
                    _compact
                        ? 1.8d
                        : 2.2d);

            var geometry =
                new StreamGeometry();

            using (var context =
                   geometry.Open())
            {
                var first =
                    _points[0];

                context.BeginFigure(
                    new Point(
                        toX(
                            first.Time),
                        toY(
                            first.Rate)),
                    false,
                    false);

                foreach (var point
                         in _points.Skip(
                             1))
                {
                    context.LineTo(
                        new Point(
                            toX(
                                point.Time),
                            toY(
                                point.Rate)),
                        true,
                        false);
                }
            }

            geometry.Freeze();

            dc.DrawGeometry(
                null,
                linePen,
                geometry);

            var pointBrush =
                new SolidColorBrush(
                    _rateColor);

            foreach (var point
                     in _points)
            {
                dc.DrawEllipse(
                    pointBrush,
                    null,
                    new Point(
                        toX(
                            point.Time),
                        toY(
                            point.Rate)),
                    _compact
                        ? 1.7d
                        : 2.3d,
                    _compact
                        ? 1.7d
                        : 2.3d);
            }
        }

        private void DrawYAxisTitle(
            DrawingContext dc,
            double top,
            double bottom,
            double pixelsPerDip)
        {
            var text =
                new FormattedText(
                    _yAxisTitle,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Segoe UI"),
                        FontStyles.Normal,
                        FontWeights.SemiBold,
                        FontStretches.Normal),
                    _compact
                        ? 7d
                        : 8.5d,
                    Brushes.Black,
                    pixelsPerDip);

            dc.PushTransform(
                new RotateTransform(
                    -90d));

            dc.DrawText(
                text,
                new Point(
                    -(
                        top
                        + bottom
                    )
                    / 2d
                    - text.Width / 2d,
                    4d));

            dc.Pop();
        }

        private void DrawLegend(
            DrawingContext dc,
            double left,
            double top,
            double width,
            double pixelsPerDip)
        {
            var x =
                left;

            var y =
                top;

            var linePen =
                new Pen(
                    new SolidColorBrush(
                        _rateColor),
                    2d);

            dc.DrawLine(
                linePen,
                new Point(
                    x,
                    y + 6d),
                new Point(
                    x + 27d,
                    y + 6d));

            var rateLabel =
                new FormattedText(
                    _rateLegendText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Segoe UI"),
                        FontStyles.Normal,
                        FontWeights.SemiBold,
                        FontStretches.Normal),
                    _compact
                        ? 7d
                        : 8.5d,
                    Brushes.Black,
                    pixelsPerDip);

            dc.DrawText(
                rateLabel,
                new Point(
                    x + 34d,
                    y));

            y +=
                _compact
                    ? 19d
                    : 23d;

            if (_plannedPoints.Count > 0)
            {
                var plannedPen =
                    new Pen(
                        new SolidColorBrush(
                            _plannedRateColor),
                        1.7d)
                    {
                        DashStyle =
                            DashStyles.Dash
                    };

                dc.DrawLine(
                    plannedPen,
                    new Point(
                        x,
                        y + 6d),
                    new Point(
                        x + 27d,
                        y + 6d));

                var plannedLabel =
                    new FormattedText(
                        _plannedRateLegendText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            new FontFamily(
                                "Segoe UI"),
                            FontStyles.Normal,
                            FontWeights.SemiBold,
                            FontStretches.Normal),
                        _compact
                            ? 7d
                            : 8.5d,
                        Brushes.Black,
                        pixelsPerDip);

                dc.DrawText(
                    plannedLabel,
                    new Point(
                        x + 34d,
                        y));

                y +=
                    _compact
                        ? 19d
                        : 23d;
            }

            if (_legend.Count == 0)
            {
                return;
            }

            var stateTitle =
                new FormattedText(
                    _stateLegendTitle,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Segoe UI"),
                        FontStyles.Normal,
                        FontWeights.SemiBold,
                        FontStretches.Normal),
                    _compact
                        ? 7d
                        : 8.5d,
                    Brushes.Black,
                    pixelsPerDip);

            dc.DrawText(
                stateTitle,
                new Point(
                    left,
                    y));

            y +=
                _compact
                    ? 16d
                    : 19d;

            var itemWidth =
                _compact
                    ? 150d
                    : 190d;

            var rowHeight =
                _compact
                    ? 16d
                    : 19d;

            var columns =
                Math.Max(
                    1,
                    (int)Math.Floor(
                        width
                        / itemWidth));

            for (var index = 0;
                 index < _legend.Count;
                 index++)
            {
                var item =
                    _legend[index];

                var column =
                    index
                    % columns;

                var row =
                    index
                    / columns;

                var itemX =
                    left
                    + column
                    * itemWidth;

                var itemY =
                    y
                    + row
                    * rowHeight;

                var size =
                    _compact
                        ? 8d
                        : 10d;

                dc.DrawRectangle(
                    new SolidColorBrush(
                        item.Color),
                    new Pen(
                        Brushes.Gray,
                        0.45d),
                    new Rect(
                        itemX,
                        itemY + 2d,
                        size,
                        size));

                var label =
                    new FormattedText(
                        item.Label,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            "Segoe UI"),
                        _compact
                            ? 6.5d
                            : 7.8d,
                        Brushes.Black,
                        pixelsPerDip)
                    {
                        MaxTextWidth =
                            itemWidth
                            - size
                            - 8d,
                        Trimming =
                            TextTrimming.CharacterEllipsis
                    };

                dc.DrawText(
                    label,
                    new Point(
                        itemX + size + 5d,
                        itemY));
            }
        }

        private static DateTime CeilToHalfHour(
            DateTime value)
        {
            var minute =
                value.Minute;

            var floorMinute =
                minute < 30
                    ? 0
                    : 30;

            var floor =
                new DateTime(
                    value.Year,
                    value.Month,
                    value.Day,
                    value.Hour,
                    floorMinute,
                    0,
                    value.Kind);

            return floor < value
                ? floor.AddMinutes(
                    30)
                : floor;
        }
    }

    private IReadOnlyList<MesReportDefinition> EnsureProductionGraphDefinition(
        IReadOnlyList<MesReportDefinition> definitions)
    {
        if (definitions.Any(definition =>
                IsProductionGraphReport(
                    definition)))
        {
            return definitions;
        }

        var states =
            definitions.FirstOrDefault(definition =>
                string.Equals(
                    definition.DataSource,
                    "States",
                    StringComparison.OrdinalIgnoreCase));

        if (states is null)
        {
            return definitions;
        }

        try
        {
            var json =
                JsonSerializer.Serialize(
                    states);

            var node =
                JsonNode.Parse(
                    json)
                as JsonObject;

            if (node is null)
            {
                return definitions;
            }

            node["Code"] =
                Mes06ProductionGraphReportCode;

            node["Name"] =
                "Production graph";

            node["NameKey"] =
                "MES06.Report.ProductionGraph.Name";

            node["Description"] =
                "Machine tact in pcs/min with exact FASTEC machine-state background.";

            node["DescriptionKey"] =
                "MES06.Report.ProductionGraph.Description";

            node["DataSource"] =
                "States";

            node["Chart"] =
                null;

            node["MaxRows"] =
                Math.Max(
                    states.MaxRows,
                    50000);

            var clone =
                node.Deserialize<MesReportDefinition>();

            if (clone is null)
            {
                return definitions;
            }

            return definitions
                .Concat(
                    new[]
                    {
                        clone
                    })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 Production Graph report definition could not be created.",
                ex);

            return definitions;
        }
    }

    private static bool IsProductionGraphReport(
        MesReportDefinition definition)
    {
        return string.Equals(
            definition.Code,
            Mes06ProductionGraphReportCode,
            StringComparison.OrdinalIgnoreCase);
    }

    private void ResetProductionGraphPresentation()
    {
        if (!_mes06ProductionGraphActive)
        {
            return;
        }

        _mes06ProductionGraphActive =
            false;

        Grid.SetRowSpan(
            ChartBorder,
            1);

        ChartBorder.Height =
            220d;

        ChartBorder.MinHeight =
            0d;

        GridReport.Visibility =
            Visibility.Visible;

        ChartHost.Content =
            null;
    }

    private void BuildProductionGraphPresentation(
        MesReportDefinition definition)
    {
        _mes06ProductionGraphActive =
            true;

        GridReport.Columns.Clear();
        GridReport.ItemsSource =
            null;
        GridReport.Visibility =
            Visibility.Collapsed;

        CounterSummaryBorder.Visibility =
            Visibility.Collapsed;

        GridCounterSummary.ItemsSource =
            null;

        Grid.SetRowSpan(
            ChartBorder,
            2);

        ChartBorder.Height =
            double.NaN;

        ChartBorder.MinHeight =
            480d;

        ChartBorder.Visibility =
            Visibility.Visible;

        var workcenter =
            GetSelectedWorkcenterCodes()
                .SingleOrDefault()
            ?? string.Empty;

        TxtChartTitle.Text =
            string.Format(
                T(
                    "MES06.Report.ProductionGraph.ChartTitle",
                    "Production graph – {0}"),
                workcenter);

        var availableScreenWidth =
            Math.Max(
                1150d,
                Math.Max(
                    ChartBorder.ActualWidth,
                    ChartHost.ActualWidth)
                - 28d);

        var element =
            CreateProductionGraphElement(
                Math.Max(
                    availableScreenWidth,
                    CalculateProductionGraphScreenWidth()),
                compact: false);

        ChartHost.Content =
            new ScrollViewer
            {
                Content =
                    element,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                CanContentScroll =
                    false,
                Background =
                    Brushes.White
            };
    }

    private double CalculateProductionGraphScreenWidth()
    {
        var bounds =
            ResolveProductionGraphBounds();

        var hours =
            Math.Max(
                0.5d,
                (
                    bounds.To
                    - bounds.From
                ).TotalHours);

        // One visual "column" every 30 minutes.
        return 105d
               + hours
               * 92d;
    }

    private (DateTime From, DateTime To) ResolveProductionGraphBounds()
    {
        var selectedShift =
            (CmbShift.SelectedItem
                as Mes06FilterChoice)
            ?.Code
            ?.Trim()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(
                selectedShift))
        {
            var shiftBounds =
                ResolveSelectedShiftTimelineBounds();

            if (shiftBounds.HasValue)
            {
                return shiftBounds.Value;
            }
        }

        if (_mes06EffectiveTo >
            _mes06EffectiveFrom)
        {
            return (
                _mes06EffectiveFrom,
                _mes06EffectiveTo);
        }

        var rows =
            _currentRows
                .OfType<Mes06ProductionGraphRecord>()
                .ToList();

        if (rows.Count == 0)
        {
            return (
                DateTime.Today,
                DateTime.Today.AddHours(
                    8));
        }

        return (
            rows.Min(row =>
                row.Starttime),
            rows.Max(row =>
                row.Endtime));
    }

    private Mes06ProductionGraphElement CreateProductionGraphElement(
        double width,
        bool compact)
    {
        var bounds =
            ResolveProductionGraphBounds();

        var rows =
            _currentRows
                .OfType<Mes06ProductionGraphRecord>()
                .Where(row =>
                    row.Endtime > bounds.From
                    && row.Starttime < bounds.To)
                .OrderBy(row =>
                    row.Starttime)
                .ToList();

        var states =
            rows
                .Select(row =>
                {
                    var colorDefinition =
                        ResolveStateColorDefinition(
                            row);

                    var color =
                        ParseFastecColor(
                            colorDefinition?.StateColor)
                        ?? ParseFastecColor(
                            colorDefinition?.CategoryColor)
                        ?? Color.FromRgb(
                            205,
                            205,
                            205);

                    return new Mes06ProductionStateSegment
                    {
                        From =
                            row.Starttime,
                        To =
                            row.Endtime,
                        StateName =
                            string.IsNullOrWhiteSpace(
                                row.StateName)
                                ? T(
                                    "MES06.Report.ProductionGraph.UnknownState",
                                    "Unknown state")
                                : row.StateName,
                        Color =
                            color
                    };
                })
                .ToList();

        var legend =
            states
                .GroupBy(
                    state =>
                        $"{state.StateName}\u001F{state.Color}",
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(group =>
                {
                    var first =
                        group.First();

                    return new Mes06ProductionLegendItem
                    {
                        Label =
                            first.StateName,
                        Color =
                            first.Color
                    };
                })
                .OrderBy(item =>
                    item.Label,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var rateIntervals =
            BuildProductionRateIntervals(
                rows,
                bounds.From,
                bounds.To);

        var plannedRateIntervals =
            BuildPlannedRateIntervals(
                rows,
                bounds.From,
                bounds.To);

        var points =
            BuildSampledRatePoints(
                rateIntervals,
                bounds.From,
                bounds.To,
                treatMissingAsZero: true);

        var plannedPoints =
            BuildSampledRatePoints(
                plannedRateIntervals,
                bounds.From,
                bounds.To,
                treatMissingAsZero: false);

        return new Mes06ProductionGraphElement(
            points,
            plannedPoints,
            states,
            legend,
            bounds.From,
            bounds.To,
            T(
                "MES06.Report.ProductionGraph.YAxis",
                "Machine tact [pcs/min]"),
            T(
                "MES06.Report.ProductionGraph.RateLegend",
                "Machine tact [pcs/min]"),
            T(
                "MES06.Report.ProductionGraph.PlannedRateLegend",
                "Planned performance [pcs/min]"),
            T(
                "MES06.Report.ProductionGraph.StateLegend",
                "Machine states"),
            width,
            compact);
    }

    private static IReadOnlyList<Mes06ProductionRateInterval> BuildProductionRateIntervals(
        IReadOnlyList<Mes06ProductionGraphRecord> rows,
        DateTime from,
        DateTime to)
    {
        return rows
            .Select(row =>
            {
                var rate =
                    row.MachineRateKsMin;

                if (!rate.HasValue
                    || rate.Value < 0m)
                {
                    return null;
                }

                var intervalFrom =
                    row.Starttime < from
                        ? from
                        : row.Starttime;

                var intervalTo =
                    row.Endtime > to
                        ? to
                        : row.Endtime;

                if (intervalTo <= intervalFrom)
                {
                    return null;
                }

                var actualRate =
                    (double)rate.Value;

                if (double.IsNaN(
                        actualRate)
                    || double.IsInfinity(
                        actualRate)
                    || actualRate < 0d)
                {
                    return null;
                }

                return new Mes06ProductionRateInterval
                {
                    From =
                        intervalFrom,
                    To =
                        intervalTo,
                    Rate =
                        actualRate
                };
            })
            .Where(interval =>
                interval is not null)
            .Select(interval =>
                interval!)
            .OrderBy(interval =>
                interval.From)
            .ToList();
    }

    private static IReadOnlyList<Mes06ProductionRateInterval> BuildPlannedRateIntervals(
        IReadOnlyList<Mes06ProductionGraphRecord> rows,
        DateTime from,
        DateTime to)
    {
        return rows
            .Select(row =>
            {
                var rate =
                    row.PlannedPerformance;

                if (!rate.HasValue
                    || rate.Value <= 0m)
                {
                    return null;
                }

                var intervalFrom =
                    row.Starttime < from
                        ? from
                        : row.Starttime;

                var intervalTo =
                    row.Endtime > to
                        ? to
                        : row.Endtime;

                if (intervalTo <= intervalFrom)
                {
                    return null;
                }

                var plannedRate =
                    (double)rate.Value;

                if (double.IsNaN(
                        plannedRate)
                    || double.IsInfinity(
                        plannedRate)
                    || plannedRate <= 0d)
                {
                    return null;
                }

                return new Mes06ProductionRateInterval
                {
                    From =
                        intervalFrom,
                    To =
                        intervalTo,
                    Rate =
                        plannedRate
                };
            })
            .Where(interval =>
                interval is not null)
            .Select(interval =>
                interval!)
            .OrderBy(interval =>
                interval.From)
            .ToList();
    }

    private static IReadOnlyList<Mes06ProductionRatePoint> BuildSampledRatePoints(
        IReadOnlyList<Mes06ProductionRateInterval> intervals,
        DateTime from,
        DateTime to,
        bool treatMissingAsZero)
    {
        if (to <= from
            || intervals.Count == 0)
        {
            return Array.Empty<Mes06ProductionRatePoint>();
        }

        var stepMinutes =
            Mes06ProductionGraphPointStepMinutes;

        var bucket =
            FloorToMinuteStep(
                from,
                stepMinutes);

        var result =
            new List<Mes06ProductionRatePoint>();

        while (bucket < to)
        {
            var bucketEnd =
                bucket.AddMinutes(
                    stepMinutes);

            var visibleFrom =
                bucket < from
                    ? from
                    : bucket;

            var visibleTo =
                bucketEnd > to
                    ? to
                    : bucketEnd;

            if (visibleTo > visibleFrom)
            {
                double weightedRate =
                    0d;

                double weight =
                    0d;

                foreach (var interval
                         in intervals)
                {
                    var overlapFrom =
                        interval.From > visibleFrom
                            ? interval.From
                            : visibleFrom;

                    var overlapTo =
                        interval.To < visibleTo
                            ? interval.To
                            : visibleTo;

                    if (overlapTo <= overlapFrom)
                    {
                        continue;
                    }

                    var overlapSeconds =
                        (
                            overlapTo
                            - overlapFrom
                        ).TotalSeconds;

                    weightedRate +=
                        interval.Rate
                        * overlapSeconds;

                    weight +=
                        overlapSeconds;
                }

                var bucketSeconds =
                    (
                        visibleTo
                        - visibleFrom
                    ).TotalSeconds;

                if (treatMissingAsZero)
                {
                    if (bucketSeconds > 0d)
                    {
                        result.Add(
                            new Mes06ProductionRatePoint
                            {
                                Time =
                                    visibleFrom
                                    + TimeSpan.FromTicks(
                                        (
                                            visibleTo
                                            - visibleFrom
                                        ).Ticks
                                        / 2),
                                Rate =
                                    weightedRate
                                    / bucketSeconds
                            });
                    }
                }
                else if (weight > 0d)
                {
                    result.Add(
                        new Mes06ProductionRatePoint
                        {
                            Time =
                                visibleFrom
                                + TimeSpan.FromTicks(
                                    (
                                        visibleTo
                                        - visibleFrom
                                    ).Ticks
                                    / 2),
                            Rate =
                                weightedRate
                                / weight
                        });
                }
            }

            bucket =
                bucketEnd;
        }

        return result;
    }

    private static DateTime FloorToMinuteStep(
        DateTime value,
        int stepMinutes)
    {
        if (stepMinutes <= 0)
        {
            stepMinutes =
                5;
        }

        var steppedMinute =
            (value.Minute / stepMinutes)
            * stepMinutes;

        return new DateTime(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            steppedMinute,
            0,
            value.Kind);
    }

    private void AppendProductionGraphToDocument(
        System.Windows.Documents.FlowDocument document)
    {
        if (_currentRows.Count == 0)
        {
            return;
        }

        var availableWidth =
            Math.Max(
                720d,
                document.PageWidth
                - document.PagePadding.Left
                - document.PagePadding.Right);

        var element =
            CreateProductionGraphElement(
                availableWidth,
                compact: true);

        element.Measure(
            new Size(
                element.Width,
                element.Height));

        element.Arrange(
            new Rect(
                0d,
                0d,
                element.Width,
                element.Height));

        element.UpdateLayout();

        const double renderDpi =
            144d;

        var scale =
            renderDpi
            / 96d;

        var bitmap =
            new RenderTargetBitmap(
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        element.Width
                        * scale)),
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        element.Height
                        * scale)),
                renderDpi,
                renderDpi,
                PixelFormats.Pbgra32);

        bitmap.Render(
            element);

        if (bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }

        var maximumDisplayHeight =
            Math.Max(
                400d,
                document.PageHeight
                - document.PagePadding.Top
                - document.PagePadding.Bottom
                - 110d);

        var displayScale =
            Math.Min(
                1d,
                maximumDisplayHeight
                / element.Height);

        var image =
            new Image
            {
                Source =
                    bitmap,
                Width =
                    availableWidth
                    * displayScale,
                Height =
                    element.Height
                    * displayScale,
                Stretch =
                    Stretch.Uniform,
                HorizontalAlignment =
                    HorizontalAlignment.Left
            };

        document.Blocks.Add(
            new System.Windows.Documents.BlockUIContainer(
                image)
            {
                Margin =
                    new Thickness(
                        0d,
                        0d,
                        0d,
                        10d)
            });
    }
}
