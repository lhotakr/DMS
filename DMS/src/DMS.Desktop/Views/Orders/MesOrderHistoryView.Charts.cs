using ClosedXML.Excel;
using DMS.Integration.Mes.Orders;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Models;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DMS.Desktop.Views.Orders;

/// <summary>MESORD read-only order history; file grouped by feature.</summary>
public sealed partial class MesOrderHistoryView
{
    private static UIElement CreateProcessTimelineV6(
        IReadOnlyList<Mes06ProcessValueRecord> messages, DateTime start, DateTime end, double chartWidth = 1120)
    {
        const double left = 240;
        double plotWidth = chartWidth - left - 20;
        const double rowHeight = 23;
        var lanes = messages.GroupBy(m => (m.WorkcenterCode, m.StateName)).ToList();
        var height = 46 + rowHeight * lanes.Count;
        var canvas = new Canvas { Width = chartWidth, Height = height,
            Background = new SolidColorBrush(Color.FromRgb(39, 44, 53)) };
        var totalSeconds = Math.Max(1, (end - start).TotalSeconds);
        for (var i = 0; i <= 4; i++)
        {
            var fraction = i / 4.0;
            var x = left + fraction * plotWidth;
            var line = new Border { Width = 1, Height = height - 13,
                Background = new SolidColorBrush(Color.FromRgb(92, 103, 118)) };
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, 15);
            canvas.Children.Add(line);
            var time = start.AddSeconds(totalSeconds * fraction);
            var tick = new TextBlock { Text = time.ToString("HH:mm"), FontSize = 12, Foreground = Brushes.WhiteSmoke };
            Canvas.SetLeft(tick, Math.Min(chartWidth - 50, x));
            Canvas.SetTop(tick, 1);
            canvas.Children.Add(tick);
        }
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var y = 28 + i * rowHeight;
            var label = new TextBlock { Text = lane.Key.WorkcenterCode + " | " + lane.Key.StateName,
                Width = left - 12, TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12, Foreground = Brushes.WhiteSmoke };
            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
            foreach (var item in lane)
            {
                var a = item.Starttime < start ? start : item.Starttime;
                var b = item.Endtime > end ? end : item.Endtime;
                if (b <= a) continue;
                var x = left + (a - start).TotalSeconds / totalSeconds * plotWidth;
                var width = Math.Max(1, (b - a).TotalSeconds / totalSeconds * plotWidth);
                var bar = new Border { Width = width, Height = rowHeight - 4,
                    Background = Brushes.LimeGreen,
                    ToolTip = $"{item.WorkcenterCode} · {item.StateName}\n{a:dd.MM.yyyy HH:mm:ss}–{b:dd.MM.yyyy HH:mm:ss}" };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y - 1);
                canvas.Children.Add(bar);
            }
        }
        return new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 450 };
    }

    // MESORD v6.3: one filtered FASTEC snapshot is passed to either report writer.

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
}
