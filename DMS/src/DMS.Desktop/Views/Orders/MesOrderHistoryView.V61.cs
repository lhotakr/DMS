using ClosedXML.Excel;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DMS.Desktop.Views.Orders;

/// <summary>
/// MESORD 6.1: historical FASTEC shifts, chronological state timeline,
/// compact report layout and themed export confirmation.
/// This code reads historical data only.
/// </summary>
public sealed partial class MesOrderHistoryView
{
    private readonly List<MesReportingShiftEvent> _mesordShiftEventsV61 = new();

    private static string ShiftTypeV61(string? raw)
    {
        var text = (raw ?? "").Trim().ToLowerInvariant();
        if (text.Contains("rann") || text.Contains("früh") || text.Contains("frueh") ||
            text.Contains("morning") || text.Contains("early")) return "Ranní";
        if (text.Contains("odpo") || text.Contains("spät") || text.Contains("spaet") ||
            text.Contains("afternoon") || text.Contains("late")) return "Odpolední";
        if (text.Contains("noč") || text.Contains("nocn") || text.Contains("nacht") ||
            text.Contains("night")) return "Noční";
        return string.Empty;
    }

    private List<MesordV6ShiftWindow> GetShiftWindowsFromDatabaseV61(bool selectedOnly = true)
    {
        var result = new List<MesordV6ShiftWindow>();
        // DimShiftEvent may contain the SAME shift once for each workplace.
        // One real shift must produce exactly one history window. Group by start
        // and normalized shift name, choosing the latest documented end.
        // Do not use the database ID for this: IDs are distinct for repeats.
        var source = _mesordShiftEventsV61
            .Where(s => s.Endtime > s.Starttime)
            .GroupBy(s => new
            {
                s.Starttime,
                Type = ShiftTypeV61(s.Name),
                Unrecognized = string.IsNullOrEmpty(ShiftTypeV61(s.Name))
                    ? (s.Name ?? string.Empty).Trim().ToUpperInvariant() : string.Empty
            })
            .Select(g => new MesReportingShiftEvent
            {
                Id = g.First().Id,
                Starttime = g.Key.Starttime,
                Endtime = g.Max(x => x.Endtime),
                Name = g.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).First().Name
            })
            .OrderBy(s => s.Starttime).ThenBy(s => s.Name).ToList();
        if (source.Count == 0) return result;

        // Historical shift-day starts with the earliest recognized morning shift,
        // not with 05:55 or an artificial midnight cut-off.
        var mornings = source.Where(s => ShiftTypeV61(s.Name) == "Ranní")
            .Select(s => s.Starttime).OrderBy(t => t).ToList();
        DateTime ShiftDay(DateTime start)
        {
            var anchor = mornings.LastOrDefault(t => t <= start);
            return anchor != default && start - anchor < TimeSpan.FromHours(30)
                ? anchor.Date : start.Date;
        }
        foreach (var dayGroup in source.GroupBy(s => ShiftDay(s.Starttime)).OrderBy(g => g.Key))
        {
            var daily = dayGroup.OrderBy(s => s.Starttime).ToList();
            for (var i = 0; i < daily.Count; i++)
            {
                var sourceShift = daily[i];
                var day = dayGroup.Key;
                if (selectedOnly && _selectedDay.HasValue && day != _selectedDay.Value.Date) continue;
                var name = ShiftTypeV61(sourceShift.Name);
                // In installations with different FASTEC shift labels, position
                // within an unambiguous three-shift day is a transparent fallback.
                // Otherwise keep unmatched shifts visible as unclassified.
                if (string.IsNullOrEmpty(name))
                    name = daily.Count == 3 ? MesordV6ShiftNames[i] : "Nezařazené";
                var from = sourceShift.Starttime > _analyticsFrom ? sourceShift.Starttime : _analyticsFrom;
                var to = sourceShift.Endtime < _analyticsTo ? sourceShift.Endtime : _analyticsTo;
                if (to <= from) continue;
                result.Add(new MesordV6ShiftWindow
                {
                    Start = from, End = to, Name = name,
                    ShiftDay = day, SourceName = sourceShift.Name
                });
            }
        }
        return result;
    }


    // All aggregate durations use the UNION of clipped time intervals, not a
    // SUM over shift-event rows. This is also safe when the database contains
    // overlapping, equivalent historical shift definitions.
    private static double UnionMinutesAcrossWindowsV62(
        IEnumerable<(DateTime from, DateTime to)> source,
        IReadOnlyList<MesordV6ShiftWindow> windows)
    {
        var slices = source.SelectMany(r => windows.Select(w =>
            (a: r.from > w.Start ? r.from : w.Start,
             b: r.to < w.End ? r.to : w.End)))
            .Where(r => r.b > r.a).OrderBy(r => r.a).ThenBy(r => r.b).ToList();
        if (slices.Count == 0) return 0d;
        var a = slices[0].a;
        var b = slices[0].b;
        var total = 0d;
        foreach (var slice in slices.Skip(1))
        {
            if (slice.a <= b)
            {
                if (slice.b > b) b = slice.b;
            }
            else
            {
                total += (b - a).TotalMinutes;
                a = slice.a;
                b = slice.b;
            }
        }
        return total + (b - a).TotalMinutes;
    }

    // Do not merge two different workplaces into one time lane; parallel
    // machines can legitimately accumulate more than one machine-hour.
    private double StateMinutesV62(IEnumerable<MesStateRecord> states,
        IReadOnlyList<MesordV6ShiftWindow> windows) =>
        states.GroupBy(s => s.WorkcenterCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Sum(g => UnionMinutesAcrossWindowsV62(
                g.Select(s => (s.Starttime, s.Endtime ?? _analyticsTo)), windows));

    // A total across ALL counter records for this order. Interpretation is
    // deliberately explicit: cumulative counters must not be presented as
    // an authoritative part count by summing snapshots.
    private static IEnumerable<object> CounterOrderSummaryV62(IEnumerable<MesCounterRecord> counters) =>
        counters.GroupBy(c => new { c.WorkcenterCode, c.CounterName })
            .Select(g => new
            {
                Pracoviště = g.Key.WorkcenterCode,
                Čítač = g.Key.CounterName,
                SoučetHodnot = Math.Round(g.Sum(c => c.Value), 2),
                Záznamů = g.Count(),
                PosledníHodnota = Math.Round(g.OrderByDescending(c => c.Timestamp).First().Value, 2)
            })
            .OrderBy(x => x.Pracoviště).ThenBy(x => x.Čítač).Cast<object>();

    private void AddCompactOrderOverviewV61(IReadOnlyList<MesStateRecord> states,
        IReadOnlyList<MesCounterRecord> counters)
    {
        var grid = new Grid { MinWidth = 890, Margin = new Thickness(0, 5, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(Label("Rozložení stavů zakázky", 17));
        left.Children.Add(PieChart(states, _analyticsFrom, _analyticsTo));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        var right = new StackPanel();
        right.Children.Add(Label("Souhrn čítačů", 17));
        right.Children.Add(Table(CounterOrderSummaryV62(counters)));
        right.Children.Add(Label("Součet všech zaznamenaných hodnot zakázky; u kumulativních čítačů není automaticky počtem kusů.", 12));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        _analyticsPanel.Children.Add(new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
    }

    private UIElement CreateShiftPieV61(IReadOnlyList<MesStateRecord> states,
        IReadOnlyList<MesordV6ShiftWindow> windows)
    {
        var values = states.GroupBy(s => s.StateName)
            .Select(g => new
            {
                Name = g.Key,
                Minutes = StateMinutesV62(g, windows),
                Brush = StateBrush(g.First())
            }).Where(x => x.Minutes > 0).OrderByDescending(x => x.Minutes).ToList();
        if (values.Count == 0) return Label("Pro tento výběr nejsou zaznamenané stavy.");
        var result = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        var canvas = new Canvas { Width = 174, Height = 174, Margin = new Thickness(0, 0, 15, 0) };
        var sum = values.Sum(x => x.Minutes);
        var angle = -90d;
        foreach (var value in values)
        {
            var sweep = 360 * value.Minutes / sum;
            if (sweep >= 359.999)
            {
                canvas.Children.Add(new Ellipse { Width = 170, Height = 170,
                    Fill = value.Brush, ToolTip = value.Name + " · " + UnitHours(value.Minutes) });
                break;
            }
            var from = angle * Math.PI / 180;
            var to = (angle + sweep) * Math.PI / 180;
            var center = new Point(85, 85);
            var path = new PathFigure { StartPoint = center, IsClosed = true, IsFilled = true };
            path.Segments.Add(new LineSegment(new Point(85 + 85 * Math.Cos(from), 85 + 85 * Math.Sin(from)), true));
            path.Segments.Add(new ArcSegment(new Point(85 + 85 * Math.Cos(to), 85 + 85 * Math.Sin(to)),
                new Size(85, 85), 0, sweep > 180, SweepDirection.Clockwise, true));
            path.Segments.Add(new LineSegment(center, true));
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { path }), Fill = value.Brush,
                ToolTip = $"{value.Name} · {UnitHours(value.Minutes)} · {100 * value.Minutes / sum:N1} %"
            });
            angle += sweep;
        }
        result.Children.Add(canvas);
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var v in values)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Swatch(v.Brush));
            line.Children.Add(Label($"{v.Name} — {UnitHours(v.Minutes)} ({100 * v.Minutes / sum:N1} %)", 12));
            legend.Children.Add(line);
        }
        result.Children.Add(legend);
        return result;
    }

    private Canvas BuildShiftSequenceCanvasV61(IReadOnlyList<MesStateRecord> states,
        DateTime from, DateTime to, double totalWidth)
    {
        const double left = 145;
        const double rowHeight = 26;
        var lanes = states.GroupBy(s => s.WorkcenterCode ?? "")
            .OrderBy(g => g.Key).ToList();
        var height = Math.Max(58, 44 + rowHeight * Math.Max(1, lanes.Count));
        var chart = new Canvas { Width = totalWidth, Height = height,
            Background = new SolidColorBrush(Color.FromRgb(38, 44, 54)) };
        var end = Math.Max(left + 60, totalWidth - 15);
        var seconds = Math.Max(1, (to - from).TotalSeconds);
        for (var i = 0; i <= 8; i++)
        {
            var fraction = i / 8d;
            var x = left + (end - left) * fraction;
            var gridLine = new Border { Width = 1, Height = height - 12,
                Background = new SolidColorBrush(Color.FromRgb(82, 94, 108)) };
            Canvas.SetLeft(gridLine, x); Canvas.SetTop(gridLine, 19); chart.Children.Add(gridLine);
            var time = from.AddSeconds(seconds * fraction);
            var mark = new TextBlock { Text = time.ToString("HH:mm"), FontSize = 11,
                Foreground = Brushes.WhiteSmoke };
            Canvas.SetLeft(mark, Math.Min(totalWidth - 37, x - 18));
            Canvas.SetTop(mark, 2); chart.Children.Add(mark);
        }
        if (lanes.Count == 0)
        {
            var empty = new TextBlock { Text = "Žádné historické stavy.", Foreground = Brushes.WhiteSmoke };
            Canvas.SetLeft(empty, 8); Canvas.SetTop(empty, 32); chart.Children.Add(empty);
        }
        for (var i = 0; i < lanes.Count; i++)
        {
            var y = 29 + i * rowHeight;
            var name = new TextBlock { Text = lanes[i].Key, FontSize = 12,
                Width = left - 12, Foreground = Brushes.WhiteSmoke,
                TextTrimming = TextTrimming.CharacterEllipsis };
            Canvas.SetLeft(name, 5); Canvas.SetTop(name, y); chart.Children.Add(name);
            foreach (var state in lanes[i].OrderBy(s => s.Starttime))
            {
                var start = state.Starttime > from ? state.Starttime : from;
                var stop = (state.Endtime ?? _analyticsTo) < to ? state.Endtime ?? _analyticsTo : to;
                if (stop <= start) continue;
                var x = left + (start - from).TotalSeconds / seconds * (end - left);
                var width = Math.Max(1, (stop - start).TotalSeconds / seconds * (end - left));
                var segment = new Border
                {
                    Width = width, Height = rowHeight - 6, Background = StateBrush(state),
                    ToolTip = $"{state.WorkcenterCode} – {state.StateName}\n{start:dd.MM.yyyy HH:mm:ss} – {stop:HH:mm:ss}\n{(stop - start).TotalMinutes:N1} min"
                };
                Canvas.SetLeft(segment, x); Canvas.SetTop(segment, y - 1);
                chart.Children.Add(segment);
            }
        }
        return chart;
    }

    private UIElement CreateShiftStateTimelineV61(IReadOnlyList<MesStateRecord> states,
        DateTime from, DateTime to)
    {
        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
        root.Children.Add(new ScrollViewer { Content = BuildShiftSequenceCanvasV61(states, from, to, 1050),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var legend = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        foreach (var g in states.GroupBy(s => s.StateName).OrderBy(g => g.Key))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 14, 0) };
            item.Children.Add(Swatch(StateBrush(g.First())));
            item.Children.Add(Label(g.Key, 12));
            legend.Children.Add(item);
        }
        root.Children.Add(legend);
        return root;
    }

    private void OfferOpenMesordExportV61(string path)
    {
        if (!File.Exists(path)) return;
        if (!DmsMessage.Confirm("MESORD – export",
                "Soubor byl úspěšně vytvořen:\n" + path + "\n\nChceš jej nyní otevřít?",
                Window.GetWindow(this))) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error("MESORD exported file could not be opened.", ex);
            DmsMessage.Error("MESORD", "Soubor se nepodařilo otevřít:\n" + ex.Message, Window.GetWindow(this));
        }
    }

    private void AppendVisualPdfV61(FlowDocument document, MesordV6Data data)
    {
        document.Blocks.Add(new Paragraph(new Bold(new Run("Grafický přehled směn")))
            { FontSize = 17, Margin = new Thickness(0, 15, 0, 8) });
        var windows = data.Windows.OrderBy(w => w.Start).ToList();
        // Render a bounded graphical summary, never thousands of source rows.
        // Complete detailed records remain available in the XLSX export.
        const int maximumShiftCharts = 36;
        var selected = windows.Take(maximumShiftCharts).ToList();
        if (windows.Count > maximumShiftCharts)
            document.Blocks.Add(new Paragraph(new Run(
                $"Zobrazeno prvních {maximumShiftCharts} z {windows.Count} směn. Kompletní přehled je v XLSX; pro podrobný PDF výkaz vyber směnový den."))
                { FontSize = 10, Margin = new Thickness(0, 4, 0, 8) });
        foreach (var name in MesordV6ShiftNames.Concat(selected.Select(w => w.Name)).Distinct())
        {
            var group = selected.Where(w => w.Name == name).ToList();
            if (group.Count == 0) continue;
            document.Blocks.Add(new Paragraph(new Bold(new Run(name + " — " + group.Count + " směn")))
                { FontSize = 14, Margin = new Thickness(0, 14, 0, 5) });
            var states = data.States.Where(s => group.Any(w => Overlap(
                s.Starttime, s.Endtime ?? _analyticsTo, w.Start, w.End))).ToList();
            document.Blocks.Add(new BlockUIContainer(CreateShiftPieV61(states, group))
                { Margin = new Thickness(0, 0, 0, 6) });
            foreach (var window in group)
            {
                var items = states.Where(s => Overlap(
                    s.Starttime, s.Endtime ?? _analyticsTo, window.Start, window.End)).ToList();
                document.Blocks.Add(new Paragraph(new Run(
                    $"{window.ShiftDay:dd.MM.yyyy} · {window.Start:HH:mm}–{window.End:HH:mm}"))
                    { FontSize = 10, Margin = new Thickness(0, 7, 0, 2) });
                document.Blocks.Add(new BlockUIContainer(
                    BuildShiftSequenceCanvasV61(items, window.Start, window.End, 680))
                    { Margin = new Thickness(0, 0, 0, 4) });
            }
        }
        document.Blocks.Add(new Paragraph(new Run(
            "Výkaz obsahuje grafický souhrn. Úplné stavové, operátorské, čítačové a procesní záznamy lze exportovat do XLSX."))
            { FontSize = 10, Margin = new Thickness(0, 12, 0, 4) });
    }

    private void FormatExcelReportV61(IXLWorksheet ws, MesordV6Data data)
    {
        ws.Range(1, 1, 1, 12).Merge();
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#293746");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        ws.Cell(1, 1).Style.Font.FontSize = 18;
        ws.Row(1).Height = 34;
        for (var metaRow = 3; metaRow <= 10; metaRow++)
            ws.Range(metaRow, 1, metaRow, 28).Merge();
        ws.Cell(12, 1).Value = "Průběh směn – historické stavy";
        ws.Cell(12, 1).Style.Font.Bold = true;
        ws.Cell(12, 1).Style.Font.FontSize = 15;
        ws.Cell(13, 1).Value = "Směna";
        ws.Cell(13, 2).Value = "Den";
        ws.Cell(13, 3).Value = "Začátek";
        ws.Cell(13, 4).Value = "Konec";
        ws.Cell(13, 5).Value = "Pracoviště";
        ws.Range(13, 1, 13, 29).Style.Fill.BackgroundColor = XLColor.FromHtml("#DDE8F0");
        ws.Range(13, 1, 13, 29).Style.Font.Bold = true;
        var row = 14;
        foreach (var window in data.Windows.OrderBy(w => w.Start))
        {
            var events = data.States.Where(s => Overlap(s.Starttime, s.Endtime ?? _analyticsTo,
                window.Start, window.End)).ToList();
            var groups = events.GroupBy(s => s.WorkcenterCode ?? "").ToList();
            // A separate timeline row for each workcenter preserves parallel states.
            // Empty shifts still produce a neutral row to make the gap explicit.
            var workcenters = groups.Count == 0 ? new[] { "" } : groups.Select(g => g.Key).ToArray();
            foreach (var workcenter in workcenters)
            {
                ws.Cell(row, 1).Value = window.Name;
                ws.Cell(row, 2).Value = window.ShiftDay;
                ws.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy";
                ws.Cell(row, 3).Value = window.Start;
                ws.Cell(row, 3).Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, 4).Value = window.End;
                ws.Cell(row, 4).Style.DateFormat.Format = "HH:mm";
                ws.Cell(row, 5).Value = string.IsNullOrEmpty(workcenter) ? "(bez stavů)" : workcenter;
                var lane = events.Where(s => string.Equals(s.WorkcenterCode ?? "", workcenter,
                    StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Starttime).ToList();
                for (var slot = 0; slot < 24; slot++)
                {
                    var midpoint = window.Start.AddSeconds(
                        (window.End - window.Start).TotalSeconds * (slot + 0.5) / 24);
                    var active = lane.Where(s => s.Starttime <= midpoint &&
                        (s.Endtime ?? _analyticsTo) > midpoint)
                        .OrderByDescending(s => s.Starttime).FirstOrDefault();
                    var cell = ws.Cell(row, slot + 6);
                    cell.Value = active == null ? "" : " ";
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
                    if (active != null && StateBrush(active) is SolidColorBrush color)
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(
                            $"#{color.Color.R:X2}{color.Color.G:X2}{color.Color.B:X2}");
                }
                ws.Row(row).Height = 23;
                row++;
            }
        }
        ws.Column(1).Width = 18;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 10;
        ws.Column(4).Width = 10;
        ws.Column(5).Width = 16;
        for (var i = 6; i <= 29; i++) ws.Column(i).Width = 3.1;
        ws.SheetView.FreezeRows(13);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
    }
}
