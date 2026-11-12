using ClosedXML.Excel;
using DMS.Integration.Mes.Orders;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DMS.Desktop.Views.Orders;

/// <summary>
/// MESORD 6.3: a single read-only data snapshot for the visible report, PDF and Excel.
/// Plant 9200 is intentionally unfilled until Quality supplies the relationship.
/// </summary>
public sealed partial class MesOrderHistoryView
{
    private MesProductionOrderRecord? _mesordExportOrderV63;
    private static readonly CultureInfo MesordCsV63 = CultureInfo.GetCultureInfo("cs-CZ");
    private static string AuthorV63 => Environment.UserName;
    private const string Plant9200V63 = "Zatím nedoplněno (doplní kvalita)";
    private static readonly XLColor HeaderColorV63 = XLColor.FromHtml("#253647");
    private static readonly XLColor PaleColorV63 = XLColor.FromHtml("#E7EEF5");
    private static readonly XLColor LineColorV63 = XLColor.FromHtml("#CBD5DF");

    private sealed class OperatorRowV63
    {
        public string Workcenter { get; init; } = "";
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTime Login { get; init; }
        public DateTime? Logout { get; init; }
        public double HoursInWindow { get; init; }
    }

    // Actual login/logout times are preserved. Only the duration is clipped to
    // the selected FASTEC shift, so cross-shift sessions are not misrepresented.
    private List<OperatorRowV63> OperatorSessionsV63(IEnumerable<MesLoggedOperatorRecord> source,
        IReadOnlyList<MesordV6ShiftWindow> windows)
    {
        var rows = new List<OperatorRowV63>();
        foreach (var o in source.Where(o => o.LoginFrom.HasValue))
        {
            var login = o.LoginFrom!.Value;
            var logoutOrNow = o.LoginTo ?? _analyticsTo;
            foreach (var w in windows)
            {
                var from = login > w.Start ? login : w.Start;
                var to = logoutOrNow < w.End ? logoutOrNow : w.End;
                if (to <= from) continue;
                rows.Add(new OperatorRowV63
                {
                    Workcenter = o.WorkcenterCode ?? "",
                    Code = o.HumanCode ?? "",
                    Name = o.OperatorName ?? "",
                    Login = login,
                    Logout = o.LoginTo,
                    HoursInWindow = (to - from).TotalHours
                });
            }
        }
        return rows.GroupBy(r => (r.Workcenter, r.Code, r.Name, r.Login, r.Logout))
            .Select(g => new OperatorRowV63
            {
                Workcenter = g.Key.Workcenter, Code = g.Key.Code, Name = g.Key.Name,
                Login = g.Key.Login, Logout = g.Key.Logout,
                HoursInWindow = g.Sum(x => x.HoursInWindow)
            }).OrderBy(x => x.Login).ThenBy(x => x.Code).ToList();
    }

    private IEnumerable<object> OperatorShiftRowsV63(IEnumerable<MesLoggedOperatorRecord> operators,
        IReadOnlyList<MesordV6ShiftWindow> windows) =>
        OperatorSessionsV63(operators, windows).Select(o => (object)new
        {
            OsobníČíslo = o.Code,
            Jméno = o.Name,
            Přihlášení = o.Login.ToString("dd.MM.yyyy HH:mm", MesordCsV63),
            Odhlášení = o.Logout?.ToString("dd.MM.yyyy HH:mm", MesordCsV63) ?? "Stále přihlášen",
            HodinVeSměně = Math.Round(o.HoursInWindow, 2)
        });

    private static void XlTitleV63(IXLWorksheet ws, string title, int row = 1)
    {
        ws.Range(row, 1, row, 12).Merge();
        var cell = ws.Cell(row, 1);
        cell.Value = title;
        cell.Style.Fill.BackgroundColor = HeaderColorV63;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 17;
        ws.Row(row).Height = 33;
    }

    private static void XlSectionV63(IXLWorksheet ws, int row, string title, int columns)
    {
        ws.Range(row, 1, row, Math.Max(columns, 5)).Merge();
        var cell = ws.Cell(row, 1);
        cell.Value = title;
        cell.Style.Fill.BackgroundColor = PaleColorV63;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = HeaderColorV63;
        ws.Row(row).Height = 23;
    }

    private static int XlTableV63(IXLWorksheet ws, int row, string[] headers,
        IEnumerable<object?[]> items)
    {
        var rows = items.ToList();
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Fill.BackgroundColor = PaleColorV63;
            cell.Style.Font.Bold = true;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorderColor = LineColorV63;
        }
        row++;
        foreach (var values in rows)
        {
            for (var c = 0; c < headers.Length && c < values.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                switch (values[c])
                {
                    case DateTime dateValue: cell.Value = dateValue; cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm"; break;
                    case double decimalValue: cell.Value = decimalValue; cell.Style.NumberFormat.Format = "0.00"; break;
                    case decimal money: cell.Value = money; cell.Style.NumberFormat.Format = "0.00"; break;
                    case int count: cell.Value = count; break;
                    default: cell.Value = Convert.ToString(values[c], MesordCsV63) ?? ""; break;
                }
                if (row % 2 == 0) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F6F9FC");
            }
            row++;
        }
        if (rows.Count == 0) ws.Cell(row++, 1).Value = "Bez záznamů";
        return row;
    }

    private IEnumerable<object?[]> StateSummaryRowsV63(IEnumerable<MesStateRecord> source,
        IReadOnlyList<MesordV6ShiftWindow> windows) =>
        source.GroupBy(s => s.StateName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new { Name = g.Key, Minutes = StateMinutesV62(g, windows) })
            .Where(x => x.Minutes > 0)
            .OrderByDescending(x => x.Minutes)
            .Select(x => new object?[] { x.Name, Math.Round(x.Minutes, 1) });

    private static IEnumerable<object?[]> CounterRowsV63(IEnumerable<MesCounterRecord> source) =>
        source.GroupBy(x => (x.WorkcenterCode, x.CounterName))
            .OrderBy(x => x.Key.WorkcenterCode).ThenBy(x => x.Key.CounterName)
            .Select(g => new object?[] { g.Key.WorkcenterCode, g.Key.CounterName,
                Math.Round(g.Sum(x => x.Value), 2) });

    private static string ExcelSheetNameV63(MesordV6ShiftWindow w, HashSet<string> existing)
    {
        var baseName = (w.ShiftDay.ToString("dd.MM.yy") + " - " + w.Name.ToLowerInvariant())
            .Replace("/", "-").Replace("\\", "-").Replace("*", "").Replace("?", "")
            .Replace("[", "(").Replace("]", ")").Replace(":", "-");
        if (baseName.Length > 27) baseName = baseName.Substring(0, 27);
        var name = baseName;
        for (var i = 2; !existing.Add(name); i++) name = baseName + "-" + i;
        return name;
    }

    private static string OrderDatesV63(DateTime from, DateTime to) =>
        $"{from:dd.MM.yyyy HH:mm} - {to:dd.MM.yyyy HH:mm}";

    private void ExcelOrderHeaderV63(IXLWorksheet ws)
    {
        ws.Cell(3, 1).Value = "Datum vytvoření";
        ws.Cell(3, 2).Value = DateTime.Now;
        ws.Cell(3, 2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
        ws.Cell(4, 1).Value = "Autor (Windows)";
        ws.Cell(4, 2).Value = AuthorV63;
        ws.Cell(5, 1).Value = "Zakázka 2000";
        ws.Cell(5, 2).Value = _orderNumber;
        ws.Cell(6, 1).Value = "Zakázka 9200";
        ws.Cell(6, 2).Value = Plant9200V63;
        ws.Cell(7, 1).Value = "Trvání zakázky";
        ws.Cell(7, 2).Value = OrderDatesV63(_analyticsFrom, _analyticsTo);
        ws.Cell(8, 1).Value = "Operace";
        ws.Cell(8, 2).Value = (_operationSelector.SelectedItem as OperationChoice)?.Caption ?? "Celá zakázka";
        if (_mesordExportOrderV63 is { } order)
        {
            ws.Cell(9, 1).Value = "Artikl / SAP";
            ws.Cell(9, 2).Value = $"{order.ProductCode} / {order.SapArticleNumber}";
            ws.Cell(10, 1).Value = "Výroba (plán / OK / NOK)";
            ws.Cell(10, 2).Value = $"{order.TargetQuantity:N0} / {order.FinishedQuantity:N0} / {order.ScrapQuantity:N0}";
        }
    }

    private static void ExcelTimelineV63(IXLWorksheet ws, int row, MesordV6ShiftWindow w,
        IReadOnlyList<MesStateRecord> states, Func<MesStateRecord, Brush> colorFor)
    {
        const int bars = 24;
        var lanes = states.GroupBy(s => s.WorkcenterCode ?? "", StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key).ToList();
        ws.Cell(row, 1).Value = "Pracoviště";
        for (var col = 0; col < bars; col++)
        {
            var moment = w.Start.AddTicks((w.End - w.Start).Ticks * col / bars);
            ws.Cell(row, col + 3).Value = moment.ToString("HH:mm");
            ws.Column(col + 3).Width = 4.6;
        }
        ws.Range(row, 1, row, 26).Style.Font.Bold = true;
        row++;
        foreach (var lane in lanes)
        {
            ws.Cell(row, 1).Value = lane.Key;
            ws.Row(row).Height = 22;
            for (var slot = 0; slot < bars; slot++)
            {
                var middle = w.Start.AddTicks((w.End - w.Start).Ticks * (slot * 2 + 1) / (bars * 2));
                var active = lane.Where(s => s.Starttime <= middle && (s.Endtime ?? w.End) > middle)
                    .OrderByDescending(s => s.Starttime).FirstOrDefault();
                var cell = ws.Cell(row, slot + 3);
                cell.Value = " ";
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5EBF0");
                if (active != null && colorFor(active) is SolidColorBrush brush)
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(
                        $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}");
            }
            row++;
        }
    }

    private static int ExcelProcessTimelineV63(IXLWorksheet ws, int row,
        MesordV6ShiftWindow w, IReadOnlyList<Mes06ProcessValueRecord> messages)
    {
        const int bars = 24;
        var lanes = messages.GroupBy(m => (m.WorkcenterCode, m.StateName))
            .OrderBy(g => g.Key.WorkcenterCode).ThenBy(g => g.Key.StateName).ToList();
        ws.Cell(row, 1).Value = "Pracoviště / proces";
        for (var col = 0; col < bars; col++)
        {
            var moment = w.Start.AddTicks((w.End - w.Start).Ticks * col / bars);
            ws.Cell(row, col + 3).Value = moment.ToString("HH:mm");
        }
        ws.Range(row, 1, row, 26).Style.Font.Bold = true;
        row++;
        foreach (var lane in lanes)
        {
            ws.Cell(row, 1).Value = lane.Key.WorkcenterCode + " / " + lane.Key.StateName;
            ws.Column(1).Width = Math.Max(ws.Column(1).Width, 32);
            for (var slot = 0; slot < bars; slot++)
            {
                var middle = w.Start.AddTicks((w.End - w.Start).Ticks * (slot * 2 + 1) / (bars * 2));
                var active = lane.Any(m => m.Starttime <= middle && m.Endtime > middle);
                var cell = ws.Cell(row, slot + 3);
                cell.Value = " ";
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(active ? "#63BD7B" : "#E5EBF0");
            }
            ws.Row(row).Height = 21;
            row++;
        }
        return row;
    }

    private void WriteExcelV63(string path, MesordV6Data data)
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add("Souhrn");
        XlTitleV63(ws, "Historie výrobní zakázky");
        ExcelOrderHeaderV63(ws);
        ws.Column(1).Width = 27;
        ws.Column(2).Width = 52;
        XlSectionV63(ws, 12, "Stavy a prostoje za zakázku", 5);
        var row = XlTableV63(ws, 13, new[] { "Stav", "Minut" }, StateSummaryRowsV63(data.States, data.Windows));
        XlSectionV63(ws, row + 2, "Čítače - součet uložených hodnot", 5);
        row = XlTableV63(ws, row + 3, new[] { "Pracoviště", "Čítač", "Součet hodnot" }, CounterRowsV63(data.Counters));
        XlSectionV63(ws, row + 2, "Seznam směn", 5);
        XlTableV63(ws, row + 3, new[] { "Datum", "Směna", "Začátek", "Konec" },
            data.Windows.OrderBy(w => w.Start).Select(w => new object?[] { w.ShiftDay.ToString("dd.MM.yyyy"), w.Name, w.Start, w.End }));
        ws.Cell(row + 1, 6).Value = "Pozn.: součet čítačů je součet historických hodnot, ne ověřený počet kusů.";
        ws.Range(row + 1, 6, row + 1, 15).Merge();
        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Souhrn" };
        foreach (var window in data.Windows.OrderBy(w => w.Start))
        {
            var tab = book.Worksheets.Add(ExcelSheetNameV63(window, used));
            XlTitleV63(tab, $"{window.ShiftDay:dd.MM.yyyy} - {window.Name}");
            tab.Cell(3, 1).Value = "Zakázka 2000";
            tab.Cell(3, 2).Value = _orderNumber;
            tab.Cell(4, 1).Value = "Zakázka 9200";
            tab.Cell(4, 2).Value = Plant9200V63;
            tab.Cell(5, 1).Value = "Skutečná směna (FASTEC)";
            tab.Cell(5, 2).Value = OrderDatesV63(window.Start, window.End);
            tab.Column(1).Width = 26;
            tab.Column(2).Width = 32;
            var states = data.States.Where(s => Overlap(s.Starttime,
                s.Endtime ?? _analyticsTo, window.Start, window.End)).ToList();
            var operators = OperatorSessionsV63(data.Operators, new[] { window });
            var counters = data.Counters.Where(c => c.Timestamp >= window.Start && c.Timestamp < window.End).ToList();
            var messages = data.Messages.Where(m => Overlap(m.Starttime,m.Endtime,window.Start,window.End)).ToList();
            XlSectionV63(tab, 7, "Průběh stavů směny (světlý motiv)", 5);
            ExcelTimelineV63(tab, 8, window, states, StateBrush);
            var nextRow = 12 + Math.Max(1, states.Select(s => s.WorkcenterCode).Distinct().Count());
            XlSectionV63(tab, nextRow, "Procesní hodnoty - časová osa MES06", 5);
            nextRow = ExcelProcessTimelineV63(tab, nextRow + 1, window, messages);
            nextRow += 2;
            XlSectionV63(tab, nextRow, "Stavy a prostoje", 5);
            nextRow = XlTableV63(tab, nextRow + 1, new[] { "Stav", "Minut" }, StateSummaryRowsV63(states, new[] { window }));
            XlSectionV63(tab, nextRow + 2, "Operátoři - skutečné přihlášení a odhlášení", 5);
            nextRow = XlTableV63(tab, nextRow + 3,
                new[] { "Pracoviště", "Osobní číslo", "Jméno", "Přihlášení", "Odhlášení", "Hodin ve směně" },
                operators.Select(o => new object?[] { o.Workcenter, o.Code, o.Name, o.Login,
                    (object?)(o.Logout?.ToString("dd.MM.yyyy HH:mm") ?? "Otevřeno"), Math.Round(o.HoursInWindow, 2) }));
            XlSectionV63(tab, nextRow + 2, "Čítače - součet hodnot za směnu", 5);
            nextRow = XlTableV63(tab, nextRow + 3, new[] { "Pracoviště", "Čítač", "Součet hodnot" }, CounterRowsV63(counters));
            XlSectionV63(tab, nextRow + 2, "Procesní hodnoty MES06", 5);
            XlTableV63(tab, nextRow + 3,
                new[] { "Pracoviště", "Stav", "Od", "Do", "Zakázka" },
                messages.Select(m => new object?[] { m.WorkcenterCode, m.StateName,
                    m.Starttime, m.Endtime, m.OrderCode }));
            tab.SheetView.FreezeRows(8);
            tab.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            tab.Column(4).Width = 21;
            tab.Column(5).Width = 21;
            tab.Column(6).Width = 20;
        }
        book.SaveAs(path);
    }

    private static TextBlock PrintTextV63(string value, int size = 11, bool strong = false) => new()
    {
        Text = value, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3)
    };

    private static System.Windows.Documents.Table PdfTableV63(string[] headers,
        IEnumerable<string[]> rows, double fontSize = 9)
    {
        var table = new System.Windows.Documents.Table { CellSpacing = 0, FontSize = fontSize, Margin = new Thickness(0, 4, 0, 8) };
        for (var i = 0; i < headers.Length; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        void AddRow(string[] values, bool header)
        {
            var row = new TableRow();
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = new TableCell(new Paragraph(new Run(i < values.Length ? values[i] : ""))
                { Margin = new Thickness(2, 3, 2, 3), Foreground = Brushes.Black });
                cell.Background = header ? new SolidColorBrush(Color.FromRgb(222, 235, 245)) : Brushes.White;
                cell.BorderBrush = Brushes.LightGray;
                cell.BorderThickness = new Thickness(0, 0, 0, .5);
                if (header) cell.FontWeight = FontWeights.SemiBold;
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        AddRow(headers, true);
        var count = 0;
        foreach (var row in rows) { AddRow(row, false); count++; }
        if (count == 0) AddRow(new[] { "Bez záznamů" }, false);
        return table;
    }

    private static Paragraph PdfTitleV63(string text, int size = 15) => new(new Bold(new Run(text)))
    { FontSize = size, Foreground = Brushes.Black, Margin = new Thickness(0, 11, 0, 5) };

    private UIElement PrintStatePieV63(IReadOnlyList<MesStateRecord> states,
        IReadOnlyList<MesordV6ShiftWindow> windows)
    {
        var values = states.GroupBy(s => s.StateName)
            .Select(g => new { Name = g.Key ?? "", Minutes = StateMinutesV62(g, windows), Color = StateBrush(g.First()) })
            .Where(g => g.Minutes > 0).OrderByDescending(g => g.Minutes).ToList();
        var root = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };
        if (values.Count == 0) { root.Children.Add(PrintTextV63("Bez záznamů stavů.")); return root; }
        var pie = new Canvas { Width = 126, Height = 126, Margin = new Thickness(2, 0, 14, 0) };
        var sum = values.Sum(v => v.Minutes);
        double angle = -90;
        foreach (var item in values)
        {
            var sweep = 360 * item.Minutes / sum;
            if (sweep >= 359.99)
            {
                pie.Children.Add(new Ellipse { Width = 124, Height = 124, Fill = item.Color });
                break;
            }
            var start = angle * Math.PI / 180;
            var stop = (angle + sweep) * Math.PI / 180;
            var center = new Point(63, 63);
            var figure = new PathFigure { StartPoint = center, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(new Point(63 + 61 * Math.Cos(start), 63 + 61 * Math.Sin(start)), true));
            figure.Segments.Add(new ArcSegment(new Point(63 + 61 * Math.Cos(stop), 63 + 61 * Math.Sin(stop)),
                new Size(61, 61), 0, sweep > 180, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(center, true));
            pie.Children.Add(new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { figure }), Fill = item.Color });
            angle += sweep;
        }
        root.Children.Add(pie);
        var legend = new StackPanel { Width = 485 };
        foreach (var item in values)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new Border { Width = 11, Height = 11, Background = item.Color, Margin = new Thickness(0, 4, 5, 0) });
            line.Children.Add(PrintTextV63($"{item.Name}: {item.Minutes:N1} min ({100 * item.Minutes / sum:N1} %)", 9));
            legend.Children.Add(line);
        }
        root.Children.Add(legend);
        return root;
    }

    private Canvas PrintShiftTimelineV63(MesordV6ShiftWindow window,
        IReadOnlyList<MesStateRecord> states)
    {
        const double left = 112;
        const double width = 680;
        const double rowHeight = 24;
        var lanes = states.GroupBy(s => s.WorkcenterCode ?? "").OrderBy(g => g.Key).ToList();
        var canvas = new Canvas { Width = width, Height = 42 + Math.Max(1, lanes.Count) * rowHeight,
            Background = Brushes.White };
        var duration = Math.Max(1, (window.End - window.Start).TotalSeconds);
        var chartWidth = width - left - 14;
        for (var i = 0; i <= 6; i++)
        {
            var x = left + chartWidth * i / 6d;
            var line = new Border { Width = .6, Height = canvas.Height - 14, Background = Brushes.LightGray };
            Canvas.SetLeft(line, x); Canvas.SetTop(line, 14); canvas.Children.Add(line);
            var mark = PrintTextV63(window.Start.AddSeconds(duration * i / 6d).ToString("HH:mm"), 9);
            Canvas.SetLeft(mark, Math.Min(width - 37, x - 16)); Canvas.SetTop(mark, 0); canvas.Children.Add(mark);
        }
        for (var index = 0; index < lanes.Count; index++)
        {
            var y = 25 + rowHeight * index;
            var label = PrintTextV63(lanes[index].Key, 10);
            label.Width = left - 6;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(label, 0); Canvas.SetTop(label, y); canvas.Children.Add(label);
            foreach (var state in lanes[index])
            {
                var start = state.Starttime > window.Start ? state.Starttime : window.Start;
                var end = (state.Endtime ?? _analyticsTo) < window.End ? (state.Endtime ?? _analyticsTo) : window.End;
                if (end <= start) continue;
                var leftX = left + (start - window.Start).TotalSeconds / duration * chartWidth;
                var w = Math.Max(1, (end - start).TotalSeconds / duration * chartWidth);
                var bar = new Border { Width = w, Height = rowHeight - 5, Background = StateBrush(state) };
                Canvas.SetLeft(bar, leftX); Canvas.SetTop(bar, y); canvas.Children.Add(bar);
            }
        }
        return canvas;
    }

    private static Canvas PrintProcessTimelineV63(MesordV6ShiftWindow window,
        IReadOnlyList<Mes06ProcessValueRecord> messages)
    {
        const double left = 170;
        const double width = 680;
        const double rowHeight = 22;
        var lanes = messages.GroupBy(m => (m.WorkcenterCode, m.StateName))
            .OrderBy(g => g.Key.WorkcenterCode).ThenBy(g => g.Key.StateName).ToList();
        var canvas = new Canvas { Width = width, Height = 40 + Math.Max(1, lanes.Count) * rowHeight,
            Background = Brushes.White };
        var duration = Math.Max(1, (window.End - window.Start).TotalSeconds);
        var chartWidth = width - left - 14;
        for (var i = 0; i <= 6; i++)
        {
            var x = left + chartWidth * i / 6d;
            var line = new Border { Width = .6, Height = canvas.Height - 14, Background = Brushes.LightGray };
            Canvas.SetLeft(line, x); Canvas.SetTop(line, 14); canvas.Children.Add(line);
            var mark = PrintTextV63(window.Start.AddSeconds(duration * i / 6d).ToString("HH:mm"), 9);
            Canvas.SetLeft(mark, Math.Min(width - 38, x - 15)); Canvas.SetTop(mark, 0); canvas.Children.Add(mark);
        }
        for (var index = 0; index < lanes.Count; index++)
        {
            var y = 23 + rowHeight * index;
            var label = PrintTextV63(lanes[index].Key.WorkcenterCode + " | " + lanes[index].Key.StateName, 9);
            label.Width = left - 8;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(label, 0); Canvas.SetTop(label, y); canvas.Children.Add(label);
            foreach (var message in lanes[index])
            {
                var start = message.Starttime > window.Start ? message.Starttime : window.Start;
                var end = message.Endtime < window.End ? message.Endtime : window.End;
                if (end <= start) continue;
                var x = left + (start - window.Start).TotalSeconds / duration * chartWidth;
                var w = Math.Max(1, (end - start).TotalSeconds / duration * chartWidth);
                var bar = new Border { Width = w, Height = rowHeight - 5,
                    Background = new SolidColorBrush(Color.FromRgb(73, 167, 96)) };
                Canvas.SetLeft(bar, x); Canvas.SetTop(bar, y); canvas.Children.Add(bar);
            }
        }
        return canvas;
    }

    private void WritePdfV63(string path, MesordV6Data data)
    {
        const double pageWidth = 793;
        const double pageHeight = 1122;
        var doc = new FlowDocument
        {
            PageWidth = pageWidth, PageHeight = pageHeight,
            PagePadding = new Thickness(42), ColumnWidth = pageWidth - 84,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            Background = Brushes.White, Foreground = Brushes.Black
        };
        doc.Blocks.Add(new Paragraph(new Run($"Vytvořeno: {DateTime.Now:dd.MM.yyyy HH:mm}    Autor (Windows): {AuthorV63}"))
            { FontSize = 10, Margin = new Thickness(0, 0, 0, 10), Foreground = Brushes.Black });
        doc.Blocks.Add(PdfTitleV63("Historie výrobní zakázky", 21));
        doc.Blocks.Add(new Paragraph(new Run($"Závod 2000: {_orderNumber}    |    Závod 9200: {Plant9200V63}"))
            { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black });
        doc.Blocks.Add(new Paragraph(new Run($"Průběh zakázky: {OrderDatesV63(_analyticsFrom, _analyticsTo)}"))
            { Margin = new Thickness(0, 4, 0, 4), Foreground = Brushes.Black });
        if (_mesordExportOrderV63 is { } order)
        {
            doc.Blocks.Add(new Paragraph(new Run($"Artikl: {order.ProductCode}  |  SAP: {order.SapArticleNumber}"))
                { Foreground = Brushes.Black });
            doc.Blocks.Add(new Paragraph(new Run(
                $"Plán: {order.TargetQuantity:N0}    OK: {order.FinishedQuantity:N0}    NOK: {order.ScrapQuantity:N0}"))
                { Foreground = Brushes.Black });
        }
        doc.Blocks.Add(PdfTitleV63("Souhrn zakázky"));
        doc.Blocks.Add(new BlockUIContainer(PrintStatePieV63(data.States, data.Windows))
            { Margin = new Thickness(0, 4, 0, 4) });
        doc.Blocks.Add(PdfTitleV63("Čítače - součet uložených hodnot", 12));
        doc.Blocks.Add(PdfTableV63(new[] { "Pracoviště", "Čítač", "Součet hodnot" },
            CounterRowsV63(data.Counters).Select(r => r.Select(x => Convert.ToString(x, MesordCsV63) ?? "").ToArray())));
        doc.Blocks.Add(new Paragraph(new Run(
            "Pozn.: součet hodnot kumulativního čítače není automaticky počtem vyrobených kusů."))
            { FontSize = 9, Foreground = Brushes.Black, Margin = new Thickness(0, 2, 0, 10) });
        foreach (var window in data.Windows.OrderBy(w => w.Start))
        {
            var states = data.States.Where(s => Overlap(s.Starttime, s.Endtime ?? _analyticsTo,
                window.Start, window.End)).ToList();
            var counters = data.Counters.Where(c => c.Timestamp >= window.Start && c.Timestamp < window.End).ToList();
            var operators = OperatorSessionsV63(data.Operators, new[] { window });
            var messages = data.Messages.Where(m => Overlap(m.Starttime, m.Endtime, window.Start, window.End)).ToList();
            var section = new Section { BreakPageBefore = true };
            section.Blocks.Add(PdfTitleV63($"{window.ShiftDay:dd.MM.yyyy} - {window.Name}", 17));
            section.Blocks.Add(new Paragraph(new Run($"FASTEC: {OrderDatesV63(window.Start, window.End)}"))
                { FontSize = 10, Foreground = Brushes.Black, Margin = new Thickness(0, 0, 0, 9) });
            section.Blocks.Add(new BlockUIContainer(PrintStatePieV63(states, new[] { window }))
                { Margin = new Thickness(0, 4, 0, 6) });
            section.Blocks.Add(PdfTitleV63("Průběh stavů směny", 12));
            section.Blocks.Add(new BlockUIContainer(PrintShiftTimelineV63(window, states))
                { Margin = new Thickness(0, 2, 0, 9) });
            section.Blocks.Add(PdfTitleV63("Stavy a prostoje", 11));
            section.Blocks.Add(PdfTableV63(new[] { "Stav", "Minut" },
                StateSummaryRowsV63(states, new[] { window })
                    .Select(r => r.Select(x => Convert.ToString(x, MesordCsV63) ?? "").ToArray())));
            section.Blocks.Add(PdfTitleV63("Operátoři - přihlášení a odhlášení", 11));
            section.Blocks.Add(PdfTableV63(new[] { "Osobní číslo", "Jméno", "Přihlášení", "Odhlášení", "Hodin ve směně" },
                operators.Select(o => new[] { o.Code, o.Name,
                    o.Login.ToString("dd.MM HH:mm"), o.Logout?.ToString("dd.MM HH:mm") ?? "Otevřeno",
                    o.HoursInWindow.ToString("N2", MesordCsV63) }), 8));
            section.Blocks.Add(PdfTitleV63("Čítače - součet hodnot za směnu", 11));
            section.Blocks.Add(PdfTableV63(new[] { "Pracoviště", "Čítač", "Součet hodnot" },
                CounterRowsV63(counters).Select(r => r.Select(x => Convert.ToString(x, MesordCsV63) ?? "").ToArray())));
            section.Blocks.Add(PdfTitleV63($"Procesní hodnoty MES06 ({messages.Count:N0})", 11));
            section.Blocks.Add(new BlockUIContainer(PrintProcessTimelineV63(window, messages))
                { Margin = new Thickness(0, 3, 0, 9) });
            doc.Blocks.Add(section);
        }
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.PageSize = new Size(pageWidth, pageHeight);
        paginator.ComputePageCount();
        // Never silently truncate shifts. A very long report is rejected with
        // a clear message rather than producing hundreds of unreadable pages.
        if (paginator.PageCount > 250)
            throw new InvalidOperationException($"PDF má {paginator.PageCount} stran. Pro menší výstup vyber směnový den.");
        var images = new List<byte[]>();
        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            if (page == null || ReferenceEquals(page, DocumentPage.Missing)) continue;
            var target = new DrawingVisual();
            using (var context = target.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));
                context.DrawRectangle(new VisualBrush(page.Visual), null, new Rect(0, 0, pageWidth, pageHeight));
            }
            var bitmap = new RenderTargetBitmap((int)pageWidth, (int)pageHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(target);
            var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            images.Add(stream.ToArray());
        }
        if (images.Count == 0) throw new InvalidOperationException("PDF nemá žádnou vykreslitelnou stránku.");
        WriteImagePdfV6(path, images, (int)pageWidth, (int)pageHeight);
    }
}
