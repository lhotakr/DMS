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
    private const string Mes06MachineTimelineReportCode =
        "MACHINE_TIMELINE";

    private DateTime _mes06EffectiveFrom;
    private DateTime _mes06EffectiveTo;
    private bool _mes06MachineTimelineActive;

    private sealed class Mes06TimelineSegment
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string StateName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public Color Color { get; init; }
    }

    private sealed class Mes06TimelineLane
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public IReadOnlyList<Mes06TimelineSegment> Segments { get; init; } =
            Array.Empty<Mes06TimelineSegment>();
    }

    private sealed class Mes06TimelineLegendItem
    {
        public string Label { get; init; } = string.Empty;
        public Color Color { get; init; }
    }

    private sealed class Mes06MachineTimelineElement
        : FrameworkElement
    {
        private readonly IReadOnlyList<Mes06TimelineLane> _lanes;
        private readonly IReadOnlyList<Mes06TimelineLegendItem> _legend;
        private readonly DateTime _from;
        private readonly DateTime _to;
        private readonly string _legendTitle;
        private readonly double _rowHeight;
        private readonly bool _compact;

        public Mes06MachineTimelineElement(
            IReadOnlyList<Mes06TimelineLane> lanes,
            IReadOnlyList<Mes06TimelineLegendItem> legend,
            DateTime from,
            DateTime to,
            string legendTitle,
            double width,
            double rowHeight,
            bool compact)
        {
            _lanes = lanes;
            _legend = legend;
            _from = from;
            _to = to;
            _legendTitle = legendTitle;
            _rowHeight = rowHeight;
            _compact = compact;

            Width = width;
            Height = CalculateDesiredHeight(
                width);
        }

        private double LeftMargin =>
            _compact
                ? 78d
                : 94d;

        private double TopMargin =>
            _compact
                ? 10d
                : 14d;

        private double AxisHeight =>
            _compact
                ? 32d
                : 38d;

        private double LegendItemWidth =>
            _compact
                ? 165d
                : 205d;

        private double LegendRowHeight =>
            _compact
                ? 17d
                : 20d;

        private double CalculateDesiredHeight(
            double width)
        {
            var plotBottom =
                TopMargin
                + _lanes.Count
                * _rowHeight;

            var legendWidth =
                Math.Max(
                    100d,
                    width
                    - LeftMargin
                    - 12d);

            var columns =
                Math.Max(
                    1,
                    (int)Math.Floor(
                        legendWidth
                        / LegendItemWidth));

            var legendRows =
                _legend.Count == 0
                    ? 0
                    : (int)Math.Ceiling(
                        _legend.Count
                        / (double)columns);

            return plotBottom
                   + AxisHeight
                   + (
                       _legend.Count == 0
                           ? 8d
                           : 24d
                             + legendRows
                             * LegendRowHeight
                             + 10d
                   );
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

            if (_to <= _from
                || _lanes.Count == 0)
            {
                return;
            }

            var pixelsPerDip =
                VisualTreeHelper.GetDpi(
                        this)
                    .PixelsPerDip;

            var plotLeft =
                LeftMargin;

            var plotRight =
                Math.Max(
                    plotLeft + 100d,
                    ActualWidth - 10d);

            var plotTop =
                TopMargin;

            var plotHeight =
                _lanes.Count
                * _rowHeight;

            var plotBottom =
                plotTop
                + plotHeight;

            var plotWidth =
                plotRight
                - plotLeft;

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

                return plotLeft
                       + Math.Clamp(
                           ratio,
                           0d,
                           1d)
                       * plotWidth;
            }

            // Base lane area.
            dc.DrawRectangle(
                new SolidColorBrush(
                    Color.FromRgb(
                        248,
                        248,
                        248)),
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            150,
                            150,
                            150)),
                    0.7d),
                new Rect(
                    plotLeft,
                    plotTop,
                    plotWidth,
                    plotHeight));

            DrawHalfHourBackground(
                dc,
                plotLeft,
                plotTop,
                plotWidth,
                plotHeight,
                ToX);

            // Horizontal workcenter rows + labels.
            var rowLinePen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            190,
                            190)),
                    0.55d);

            for (var index = 0;
                 index < _lanes.Count;
                 index++)
            {
                var lane =
                    _lanes[index];

                var y =
                    plotTop
                    + index
                    * _rowHeight;

                if (index > 0)
                {
                    dc.DrawLine(
                        rowLinePen,
                        new Point(
                            plotLeft,
                            y),
                        new Point(
                            plotRight,
                            y));
                }

                var label =
                    new FormattedText(
                        lane.WorkcenterCode,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            "Segoe UI"),
                        _compact
                            ? 7.2d
                            : 9d,
                        Brushes.Black,
                        pixelsPerDip)
                    {
                        MaxTextWidth =
                            LeftMargin - 10d,
                        Trimming =
                            TextTrimming.CharacterEllipsis
                    };

                dc.DrawText(
                    label,
                    new Point(
                        4d,
                        y
                        + Math.Max(
                            0d,
                            (
                                _rowHeight
                                - label.Height
                            )
                            / 2d)));
            }

            // FASTEC state intervals.
            foreach (var laneIndex
                     in Enumerable.Range(
                         0,
                         _lanes.Count))
            {
                var lane =
                    _lanes[laneIndex];

                var top =
                    plotTop
                    + laneIndex
                    * _rowHeight
                    + 1d;

                var height =
                    Math.Max(
                        1d,
                        _rowHeight - 2d);

                foreach (var segment
                         in lane.Segments)
                {
                    var from =
                        segment.From < _from
                            ? _from
                            : segment.From;

                    var to =
                        segment.To > _to
                            ? _to
                            : segment.To;

                    if (to <= from)
                    {
                        continue;
                    }

                    var x1 =
                        ToX(
                            from);

                    var x2 =
                        ToX(
                            to);

                    var width =
                        Math.Max(
                            1.15d,
                            x2 - x1);

                    var fill =
                        new SolidColorBrush(
                            segment.Color);

                    var borderColor =
                        Color.FromArgb(
                            185,
                            (byte)Math.Max(
                                0,
                                segment.Color.R - 45),
                            (byte)Math.Max(
                                0,
                                segment.Color.G - 45),
                            (byte)Math.Max(
                                0,
                                segment.Color.B - 45));

                    dc.DrawRectangle(
                        fill,
                        new Pen(
                            new SolidColorBrush(
                                borderColor),
                            0.45d),
                        new Rect(
                            x1,
                            top,
                            width,
                            height));
                }
            }

            DrawTimeAxis(
                dc,
                plotLeft,
                plotBottom,
                plotWidth,
                ToX,
                pixelsPerDip);

            DrawLegend(
                dc,
                plotLeft,
                plotBottom
                + AxisHeight,
                plotRight - plotLeft,
                pixelsPerDip);
        }

        private void DrawHalfHourBackground(
            DrawingContext dc,
            double plotLeft,
            double plotTop,
            double plotWidth,
            double plotHeight,
            Func<DateTime, double> toX)
        {
            var tick =
                FloorToHalfHour(
                    _from);

            var alternatingBrush =
                new SolidColorBrush(
                    Color.FromArgb(
                        72,
                        218,
                        218,
                        218));

            var halfHourPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            215,
                            215,
                            215)),
                    0.45d);

            var fullHourPen =
                new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            165,
                            165,
                            165)),
                    0.9d);

            var slot =
                0;

            while (tick < _to)
            {
                var next =
                    tick.AddMinutes(
                        30);

                var visibleFrom =
                    tick < _from
                        ? _from
                        : tick;

                var visibleTo =
                    next > _to
                        ? _to
                        : next;

                if (visibleTo > visibleFrom)
                {
                    if (slot % 2 == 0)
                    {
                        dc.DrawRectangle(
                            alternatingBrush,
                            null,
                            new Rect(
                                toX(
                                    visibleFrom),
                                plotTop,
                                Math.Max(
                                    0d,
                                    toX(
                                        visibleTo)
                                    - toX(
                                        visibleFrom)),
                                plotHeight));
                    }

                    if (tick >= _from)
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
                                plotTop),
                            new Point(
                                x,
                                plotTop
                                + plotHeight));
                    }
                }

                tick =
                    next;

                slot++;
            }

            dc.DrawLine(
                fullHourPen,
                new Point(
                    plotLeft + plotWidth,
                    plotTop),
                new Point(
                    plotLeft + plotWidth,
                    plotTop + plotHeight));
        }

        private void DrawTimeAxis(
            DrawingContext dc,
            double plotLeft,
            double plotBottom,
            double plotWidth,
            Func<DateTime, double> toX,
            double pixelsPerDip)
        {
            var axisPen =
                new Pen(
                    Brushes.Gray,
                    0.7d);

            dc.DrawLine(
                axisPen,
                new Point(
                    plotLeft,
                    plotBottom),
                new Point(
                    plotLeft + plotWidth,
                    plotBottom));

            var spanHours =
                (
                    _to
                    - _from
                ).TotalHours;

            var labelStepHours =
                spanHours <= 12d
                    ? 1
                    : spanHours <= 30d
                        ? 2
                        : spanHours <= 72d
                            ? 4
                            : 8;

            var tick =
                CeilToHour(
                    _from);

            while (tick < _to)
            {
                if (tick.Hour
                    % labelStepHours
                    == 0)
                {
                    var x =
                        toX(
                            tick);

                    var labelText =
                        spanHours > 26d
                            ? tick.ToString(
                                "dd.MM HH:mm",
                                CultureInfo.CurrentCulture)
                            : tick.ToString(
                                "HH:mm",
                                CultureInfo.CurrentCulture);

                    var label =
                        new FormattedText(
                            labelText,
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
                            plotBottom + 5d));
                }

                tick =
                    tick.AddHours(
                        1);
            }

            var fromText =
                new FormattedText(
                    _from.ToString(
                        "dd.MM HH:mm",
                        CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        "Segoe UI"),
                    _compact
                        ? 6.6d
                        : 7.6d,
                    Brushes.DimGray,
                    pixelsPerDip);

            dc.DrawText(
                fromText,
                new Point(
                    plotLeft,
                    plotBottom
                    + AxisHeight
                    - fromText.Height
                    - 2d));

            var toText =
                new FormattedText(
                    _to.ToString(
                        "dd.MM HH:mm",
                        CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        "Segoe UI"),
                    _compact
                        ? 6.6d
                        : 7.6d,
                    Brushes.DimGray,
                    pixelsPerDip);

            dc.DrawText(
                toText,
                new Point(
                    plotLeft
                    + plotWidth
                    - toText.Width,
                    plotBottom
                    + AxisHeight
                    - toText.Height
                    - 2d));
        }

        private void DrawLegend(
            DrawingContext dc,
            double left,
            double top,
            double width,
            double pixelsPerDip)
        {
            if (_legend.Count == 0)
            {
                return;
            }

            var title =
                new FormattedText(
                    _legendTitle,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Segoe UI"),
                        FontStyles.Normal,
                        FontWeights.SemiBold,
                        FontStretches.Normal),
                    _compact
                        ? 7.5d
                        : 9d,
                    Brushes.Black,
                    pixelsPerDip);

            dc.DrawText(
                title,
                new Point(
                    left,
                    top));

            var contentTop =
                top
                + title.Height
                + 4d;

            var columns =
                Math.Max(
                    1,
                    (int)Math.Floor(
                        width
                        / LegendItemWidth));

            for (var index = 0;
                 index < _legend.Count;
                 index++)
            {
                var row =
                    index
                    / columns;

                var column =
                    index
                    % columns;

                var x =
                    left
                    + column
                    * LegendItemWidth;

                var y =
                    contentTop
                    + row
                    * LegendRowHeight;

                var item =
                    _legend[index];

                var swatchSize =
                    _compact
                        ? 9d
                        : 12d;

                dc.DrawRectangle(
                    new SolidColorBrush(
                        item.Color),
                    new Pen(
                        Brushes.Gray,
                        0.45d),
                    new Rect(
                        x,
                        y + 2d,
                        swatchSize,
                        swatchSize));

                var label =
                    new FormattedText(
                        item.Label,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(
                            "Segoe UI"),
                        _compact
                            ? 6.8d
                            : 8.2d,
                        Brushes.Black,
                        pixelsPerDip)
                    {
                        MaxTextWidth =
                            LegendItemWidth
                            - swatchSize
                            - 8d,
                        Trimming =
                            TextTrimming.CharacterEllipsis
                    };

                dc.DrawText(
                    label,
                    new Point(
                        x
                        + swatchSize
                        + 5d,
                        y));
            }
        }

        private static DateTime FloorToHalfHour(
            DateTime value)
        {
            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute < 30
                    ? 0
                    : 30,
                0,
                value.Kind);
        }

        private static DateTime CeilToHour(
            DateTime value)
        {
            var floor =
                new DateTime(
                    value.Year,
                    value.Month,
                    value.Day,
                    value.Hour,
                    0,
                    0,
                    value.Kind);

            return floor < value
                ? floor.AddHours(
                    1)
                : floor;
        }
    }

    private IReadOnlyList<MesReportDefinition> EnsureMachineTimelineDefinition(
        IReadOnlyList<MesReportDefinition> definitions)
    {
        if (definitions.Any(definition =>
                IsMachineTimelineReport(
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
                Mes06MachineTimelineReportCode;

            node["Name"] =
                "Machine timeline";

            node["NameKey"] =
                "MES06.Report.MachineTimeline.Name";

            node["Description"] =
                "Timeline of FASTEC machine states with dynamic legend.";

            node["DescriptionKey"] =
                "MES06.Report.MachineTimeline.Description";

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
                "MES06 Machine timeline report definition could not be created.",
                ex);

            return definitions;
        }
    }

    private static bool IsMachineTimelineReport(
        MesReportDefinition definition)
    {
        return string.Equals(
            definition.Code,
            Mes06MachineTimelineReportCode,
            StringComparison.OrdinalIgnoreCase);
    }

    private void ResetMachineTimelinePresentation()
    {
        if (!_mes06MachineTimelineActive)
        {
            return;
        }

        _mes06MachineTimelineActive =
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

    private void BuildMachineTimelinePresentation(
        MesReportDefinition definition)
    {
        _mes06MachineTimelineActive =
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
            430d;

        ChartBorder.Visibility =
            Visibility.Visible;

        TxtChartTitle.Text =
            T(
                "MES06.Report.MachineTimeline.ChartTitle",
                "Machine timeline");

        var availableScreenWidth =
            Math.Max(
                1180d,
                Math.Max(
                    ChartBorder.ActualWidth,
                    ChartHost.ActualWidth)
                - 28d);

        var element =
            CreateMachineTimelineElement(
                Math.Max(
                    availableScreenWidth,
                    CalculateMachineTimelineScreenWidth()),
                compact: false);

        var scrollViewer =
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

        ChartHost.Content =
            scrollViewer;
    }

    private double CalculateMachineTimelineScreenWidth()
    {
        var from =
            ResolveMachineTimelineFrom();

        var to =
            ResolveMachineTimelineTo();

        var hours =
            Math.Max(
                1d,
                (
                    to
                    - from
                ).TotalHours);

        return 120d
               + hours
               * 42d;
    }

    private Mes06MachineTimelineElement CreateMachineTimelineElement(
        double width,
        bool compact)
    {
        var from =
            ResolveMachineTimelineFrom();

        var to =
            ResolveMachineTimelineTo();

        var lanes =
            BuildMachineTimelineLanes(
                from,
                to);

        var legend =
            lanes
                .SelectMany(lane =>
                    lane.Segments)
                .GroupBy(
                    segment =>
                        $"{segment.StateName}\u001F{segment.Color}",
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(group =>
                {
                    var first =
                        group.First();

                    return new Mes06TimelineLegendItem
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

        var rowHeight =
            compact
                ? CalculateCompactTimelineRowHeight(
                    lanes.Count)
                : 21d;

        return new Mes06MachineTimelineElement(
            lanes,
            legend,
            from,
            to,
            T(
                "MES06.Report.MachineTimeline.Legend",
                "Legend"),
            width,
            rowHeight,
            compact);
    }

    private static double CalculateCompactTimelineRowHeight(
        int laneCount)
    {
        if (laneCount <= 30)
        {
            return 13d;
        }

        if (laneCount <= 40)
        {
            return 11d;
        }

        return Math.Max(
            8d,
            430d
            / Math.Max(
                1,
                laneCount));
    }

    private IReadOnlyList<Mes06TimelineLane> BuildMachineTimelineLanes(
        DateTime from,
        DateTime to)
    {
        var selectedCodes =
            GetSelectedWorkcenterCodes()
                .Where(code =>
                    !string.IsNullOrWhiteSpace(
                        code))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    code =>
                        code,
                    Comparer<string>.Create(
                        CompareNaturalWorkcenterCodes))
                .ToList();

        if (selectedCodes.Count == 0)
        {
            selectedCodes =
                _currentRows
                    .Select(
                        ResolveStateWorkcenter)
                    .Where(code =>
                        !string.IsNullOrWhiteSpace(
                            code))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        code =>
                            code,
                        Comparer<string>.Create(
                            CompareNaturalWorkcenterCodes))
                    .ToList();
        }

        var segmentsByWorkcenter =
            new Dictionary<string, List<Mes06TimelineSegment>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var row
                 in _currentRows)
        {
            var workcenter =
                ResolveStateWorkcenter(
                    row);

            if (string.IsNullOrWhiteSpace(
                    workcenter))
            {
                continue;
            }

            var start =
                FirstDateTime(
                    ReadProperty(
                        row,
                        "From"),
                    ReadProperty(
                        row,
                        "Starttime"),
                    ReadProperty(
                        row,
                        "StartTime"));

            var end =
                FirstDateTime(
                    ReadProperty(
                        row,
                        "To"),
                    ReadProperty(
                        row,
                        "Endtime"),
                    ReadProperty(
                        row,
                        "EndTime"));

            if (!start.HasValue
                || !end.HasValue)
            {
                continue;
            }

            var visibleFrom =
                start.Value < from
                    ? from
                    : start.Value;

            var visibleTo =
                end.Value > to
                    ? to
                    : end.Value;

            if (visibleTo <= visibleFrom)
            {
                continue;
            }

            var state =
                ResolveStateName(
                    row);

            var category =
                ResolveStateCategory(
                    row);

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

            if (!segmentsByWorkcenter.TryGetValue(
                    workcenter,
                    out var segments))
            {
                segments =
                    new List<Mes06TimelineSegment>();

                segmentsByWorkcenter[
                    workcenter] =
                    segments;
            }

            segments.Add(
                new Mes06TimelineSegment
                {
                    WorkcenterCode =
                        workcenter,
                    StateName =
                        string.IsNullOrWhiteSpace(
                            state)
                            ? T(
                                "MES06.Report.MachineTimeline.UnknownState",
                                "Unknown state")
                            : state,
                    CategoryName =
                        category,
                    From =
                        visibleFrom,
                    To =
                        visibleTo,
                    Color =
                        color
                });
        }

        return selectedCodes
            .Select(code =>
                new Mes06TimelineLane
                {
                    WorkcenterCode =
                        code,
                    Segments =
                        segmentsByWorkcenter.TryGetValue(
                            code,
                            out var segments)
                            ? segments
                                .OrderBy(segment =>
                                    segment.From)
                                .ToList()
                            : Array.Empty<Mes06TimelineSegment>()
                })
            .ToList();
    }

    private DateTime ResolveMachineTimelineFrom()
    {
        var shiftBounds =
            ResolveSelectedShiftTimelineBounds();

        if (shiftBounds.HasValue)
        {
            return shiftBounds.Value.From;
        }

        if (_mes06EffectiveFrom !=
                default)
        {
            return _mes06EffectiveFrom;
        }

        var first =
            _currentRows
                .Select(
                    ResolveRowStart)
                .Where(value =>
                    value !=
                    default)
                .DefaultIfEmpty(
                    DateTime.Today)
                .Min();

        return first;
    }

    private DateTime ResolveMachineTimelineTo()
    {
        var shiftBounds =
            ResolveSelectedShiftTimelineBounds();

        if (shiftBounds.HasValue)
        {
            return shiftBounds.Value.To;
        }

        if (_mes06EffectiveTo >
            _mes06EffectiveFrom)
        {
            return _mes06EffectiveTo;
        }

        var last =
            _currentRows
                .Select(row =>
                    FirstDateTime(
                        ReadProperty(
                            row,
                            "To"),
                        ReadProperty(
                            row,
                            "Endtime"),
                        ReadProperty(
                            row,
                            "EndTime")))
                .Where(value =>
                    value.HasValue)
                .Select(value =>
                    value!.Value)
                .DefaultIfEmpty(
                    ResolveMachineTimelineFrom()
                    .AddHours(
                        8))
                .Max();

        return last;
    }

    private (DateTime From, DateTime To)? ResolveSelectedShiftTimelineBounds()
    {
        var selectedShift =
            (CmbShift.SelectedItem
                as Mes06FilterChoice)
            ?.Code
            ?.Trim()
            ?? string.Empty;

        // "All shifts" intentionally keeps the complete production day.
        if (string.IsNullOrWhiteSpace(
                selectedShift))
        {
            return null;
        }

        var effectiveFrom =
            _mes06EffectiveFrom !=
                default
                ? _mes06EffectiveFrom
                : DateTime.MinValue;

        var effectiveTo =
            _mes06EffectiveTo >
                effectiveFrom
                ? _mes06EffectiveTo
                : DateTime.MaxValue;

        var matchingShifts =
            _mes06ShiftEvents
                .Where(shift =>
                    string.Equals(
                        shift.Name,
                        selectedShift,
                        StringComparison.CurrentCultureIgnoreCase))
                .Where(shift =>
                    shift.Endtime > effectiveFrom
                    && shift.Starttime < effectiveTo)
                .OrderBy(shift =>
                    shift.Starttime)
                .ToList();

        if (matchingShifts.Count > 0)
        {
            var from =
                matchingShifts.Min(shift =>
                    shift.Starttime);

            var to =
                matchingShifts.Max(shift =>
                    shift.Endtime);

            from =
                from < effectiveFrom
                    ? effectiveFrom
                    : from;

            to =
                to > effectiveTo
                    ? effectiveTo
                    : to;

            if (to > from)
            {
                return (
                    from,
                    to);
            }
        }

        // Fallback for unusual/legacy rows where shift-event enrichment
        // is unavailable: fit the axis to the already shift-filtered rows.
        var rowStarts =
            _currentRows
                .Select(
                    ResolveRowStart)
                .Where(value =>
                    value !=
                    default)
                .ToList();

        var rowEnds =
            _currentRows
                .Select(row =>
                    FirstDateTime(
                        ReadProperty(
                            row,
                            "To"),
                        ReadProperty(
                            row,
                            "Endtime"),
                        ReadProperty(
                            row,
                            "EndTime")))
                .Where(value =>
                    value.HasValue)
                .Select(value =>
                    value!.Value)
                .ToList();

        if (rowStarts.Count > 0
            && rowEnds.Count > 0)
        {
            var from =
                rowStarts.Min();

            var to =
                rowEnds.Max();

            if (to > from)
            {
                return (
                    from,
                    to);
            }
        }

        return null;
    }

    private void AppendMachineTimelineToDocument(
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
            CreateMachineTimelineElement(
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

        var pixelWidth =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    element.Width
                    * scale));

        var pixelHeight =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    element.Height
                    * scale));

        var bitmap =
            new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
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
                420d,
                document.PageHeight
                - document.PagePadding.Top
                - document.PagePadding.Bottom
                - 105d);

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

    private static int CompareNaturalWorkcenterCodes(
        string? left,
        string? right)
    {
        if (ReferenceEquals(
                left,
                right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftIndex =
            0;

        var rightIndex =
            0;

        while (leftIndex < left.Length
               && rightIndex < right.Length)
        {
            var leftChar =
                left[leftIndex];

            var rightChar =
                right[rightIndex];

            if (char.IsDigit(
                    leftChar)
                && char.IsDigit(
                    rightChar))
            {
                long leftNumber =
                    0;

                long rightNumber =
                    0;

                while (leftIndex < left.Length
                       && char.IsDigit(
                           left[leftIndex]))
                {
                    leftNumber =
                        leftNumber
                        * 10
                        + (
                            left[leftIndex]
                            - '0'
                        );

                    leftIndex++;
                }

                while (rightIndex < right.Length
                       && char.IsDigit(
                           right[rightIndex]))
                {
                    rightNumber =
                        rightNumber
                        * 10
                        + (
                            right[rightIndex]
                            - '0'
                        );

                    rightIndex++;
                }

                var numericComparison =
                    leftNumber.CompareTo(
                        rightNumber);

                if (numericComparison != 0)
                {
                    return numericComparison;
                }

                continue;
            }

            var charComparison =
                char.ToUpperInvariant(
                        leftChar)
                    .CompareTo(
                        char.ToUpperInvariant(
                            rightChar));

            if (charComparison != 0)
            {
                return charComparison;
            }

            leftIndex++;
            rightIndex++;
        }

        return left.Length.CompareTo(
            right.Length);
    }
}
