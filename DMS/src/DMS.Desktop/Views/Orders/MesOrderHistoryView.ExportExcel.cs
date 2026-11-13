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
    private static readonly XLColor HeaderColorV63 = XLColor.FromHtml("#253647");

    private static readonly XLColor PaleColorV63 = XLColor.FromHtml("#E7EEF5");

    private static readonly XLColor LineColorV63 = XLColor.FromHtml("#CBD5DF");


    private void XlTitleV63(IXLWorksheet ws, string title, int row = 1)
    {
        ws.Range(row, 1, row, 12).Merge();
        var cell = ws.Cell(row, 1);
        cell.Value = MesordText(title);
        cell.Style.Fill.BackgroundColor = HeaderColorV63;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 17;
        ws.Row(row).Height = 33;
    }


    private void XlSectionV63(IXLWorksheet ws, int row, string title, int columns)
    {
        ws.Range(row, 1, row, Math.Max(columns, 5)).Merge();
        var cell = ws.Cell(row, 1);
        cell.Value = MesordText(title);
        cell.Style.Fill.BackgroundColor = PaleColorV63;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = HeaderColorV63;
        ws.Row(row).Height = 23;
    }


    private int XlTableV63(IXLWorksheet ws, int row, string[] headers,
        IEnumerable<object?[]> items)
    {
        var rows = items.ToList();
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = MesordText(headers[c]);
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
        if (rows.Count == 0) ws.Cell(row++, 1).Value = MesordText("Bez záznamů");
        return row;
    }


    private string ExcelSheetNameV63(MesordV6ShiftWindow w, HashSet<string> existing)
    {
        var baseName = (w.ShiftDay.ToString("dd.MM.yy") + " - " + MesordText(w.Name).ToLowerInvariant())
            .Replace("/", "-").Replace("\\", "-").Replace("*", "").Replace("?", "")
            .Replace("[", "(").Replace("]", ")").Replace(":", "-");
        if (baseName.Length > 27) baseName = baseName.Substring(0, 27);
        var name = baseName;
        for (var i = 2; !existing.Add(name); i++) name = baseName + "-" + i;
        return name;
    }


    private void ExcelOrderHeaderV63(IXLWorksheet ws)
    {
        ws.Cell(3, 1).Value = MesordText("Datum vytvoření");
        ws.Cell(3, 2).Value = DateTime.Now;
        ws.Cell(3, 2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
        ws.Cell(4, 1).Value = MesordText("Autor (Windows)");
        ws.Cell(4, 2).Value = AuthorV63;
        ws.Cell(5, 1).Value = MesordText("Zakázka 2000");
        ws.Cell(5, 2).Value = _orderNumber;
        ws.Cell(6, 1).Value = MesordText("Zakázka 9200");
        ws.Cell(6, 2).Value = Qa9200ForMes();
        ws.Cell(7, 1).Value = MesordText("Trvání zakázky");
        ws.Cell(7, 2).Value = OrderDatesV63(_analyticsFrom, _analyticsTo);
        ws.Cell(8, 1).Value = MesordText("Operace");
        ws.Cell(8, 2).Value = (_operationSelector.SelectedItem as OperationChoice)?.Caption ?? MesordText("Celá zakázka");
        if (_mesordExportOrderV63 is { } order)
        {
            ws.Cell(9, 1).Value = MesordText("Artikl / SAP");
            ws.Cell(9, 2).Value = $"{order.ProductCode} / {order.SapArticleNumber}";
            ws.Cell(10, 1).Value = MesordText("Výroba (plán / OK / NOK)");
            ws.Cell(10, 2).Value = $"{order.TargetQuantity:N0} / {order.FinishedQuantity:N0} / {order.ScrapQuantity:N0}";
        }
    }


    private void ExcelTimelineV63(IXLWorksheet ws, int row, MesordV6ShiftWindow w,
        IReadOnlyList<MesStateRecord> states, Func<MesStateRecord, Brush> colorFor)
    {
        const int bars = 24;
        var lanes = states.GroupBy(s => s.WorkcenterCode ?? "", StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key).ToList();
        ws.Cell(row, 1).Value = MesordText("Pracoviště");
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


    private int ExcelProcessTimelineV63(IXLWorksheet ws, int row,
        MesordV6ShiftWindow w, IReadOnlyList<Mes06ProcessValueRecord> messages)
    {
        const int bars = 24;
        var lanes = messages.GroupBy(m => (m.WorkcenterCode, m.StateName))
            .OrderBy(g => g.Key.WorkcenterCode).ThenBy(g => g.Key.StateName).ToList();
        ws.Cell(row, 1).Value = MesordText("Pracoviště / proces");
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


    // The exact same chart view is rasterized for the Excel sheet, while the
    // numeric table remains native, filterable Excel cells.
    private byte[] RenderReportChartPngV64(UIElement chart, out int displayHeight)
    {
        const int logicalWidth = 680;
        chart.Measure(new Size(logicalWidth, double.PositiveInfinity));
        displayHeight = (int)Math.Ceiling(Math.Max(160, chart.DesiredSize.Height + 12));
        chart.Arrange(new Rect(0, 0, logicalWidth, displayHeight));
        var bitmap = new RenderTargetBitmap(logicalWidth * 2, displayHeight * 2,
            192, 192, PixelFormats.Pbgra32);
        bitmap.Render(chart);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private int AddExcelStatePieV64(IXLWorksheet worksheet, int row,
        IReadOnlyList<MesStateRecord> states, IReadOnlyList<MesordV6ShiftWindow> windows,
        IList<MemoryStream> retainedStreams)
    {
        var pictureBytes = RenderReportChartPngV64(PrintStatePieV63(states, windows),
            out var originalHeight);
        var stream = new MemoryStream(pictureBytes);
        retainedStreams.Add(stream);
        var picture = worksheet.AddPicture(stream, ClosedXML.Excel.Drawings.XLPictureFormat.Png);
        picture.MoveTo(worksheet.Cell(row, 3));
        const int targetWidth = 540;
        picture.Width = targetWidth;
        picture.Height = (int)Math.Ceiling(originalHeight * targetWidth / 680d);
        return row + (int)Math.Ceiling(picture.Height / 19d) + 1;
    }

    private void WriteExcelV63(string path, MesordV6Data data)
    {
        using var book = new XLWorkbook();
        var retainedStreams = new List<MemoryStream>();
        var ws = book.Worksheets.Add(MesordText("Souhrn"));
        XlTitleV63(ws, "Historie výrobní zakázky");
        ExcelOrderHeaderV63(ws);
        ws.Column(1).Width = 27;
        ws.Column(2).Width = 52;
        XlSectionV63(ws, 12, "Stavy a prostoje za zakázku", 5);
        var row = XlTableV63(ws, 13, new[] { "Stav", "Minut" }, StateSummaryRowsV63(data.States, WholeOrderWindow()));
        var pieLastRow = AddExcelStatePieV64(ws, 13, data.States, WholeOrderWindow(), retainedStreams);
        row = Math.Max(row, pieLastRow);
        XlSectionV63(ws, row + 2, "Čítače - součet uložených hodnot", 5);
        row = XlTableV63(ws, row + 3, new[] { "Pracoviště", "Čítač", "Součet hodnot" }, CounterRowsV63(data.Counters));
        XlSectionV63(ws, row + 2, "Seznam směn", 5);
        row = XlTableV63(ws, row + 3, new[] { "Datum", "Směna", "Začátek", "Konec" },
            data.Windows.OrderBy(w => w.Start).Select(w => new object?[] { w.ShiftDay.ToString("dd.MM.yyyy"), w.Name, w.Start, w.End }));
        ws.Cell(row + 1, 1).Value = MesordText("Pozn.: součet čítačů je součet historických hodnot, ne ověřený počet kusů.");
        ws.Range(row + 1, 1, row + 1, 12).Merge();
        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MesordText("Souhrn") };
        foreach (var window in data.Windows.OrderBy(w => w.Start))
        {
            var tab = book.Worksheets.Add(ExcelSheetNameV63(window, used));
            XlTitleV63(tab, $"{window.ShiftDay:dd.MM.yyyy} - {MesordText(window.Name)}");
            tab.Cell(3, 1).Value = MesordText("Zakázka 2000");
            tab.Cell(3, 2).Value = _orderNumber;
            tab.Cell(4, 1).Value = MesordText("Zakázka 9200");
            tab.Cell(4, 2).Value = Qa9200ForMes();
            tab.Cell(5, 1).Value = MesordText("Skutečná směna (FASTEC)");
            tab.Cell(5, 2).Value = OrderDatesV63(window.Start, window.End);
            tab.Column(1).Width = 26;
            tab.Column(2).Width = 32;
            var states = data.States.Where(s => Overlap(s.Starttime,
                s.Endtime ?? _analyticsTo, window.Start, window.End)).ToList();
            var operators = OperatorSessionsV63(data.Operators, new[] { window });
            var counters = data.Counters.Where(c => c.Timestamp >= window.Start && c.Timestamp < window.End).ToList();
            var messages = data.Messages.Where(m => Overlap(m.Starttime,m.Endtime,window.Start,window.End)).ToList();
            XlSectionV63(tab, 7, "Stavy a prostoje", 5);
            var pieEndRow = AddExcelStatePieV64(tab, 8, states, new[] { window }, retainedStreams);
            var timelineRow = pieEndRow + 2;
            XlSectionV63(tab, timelineRow, "Průběh stavů směny (světlý motiv)", 5);
            ExcelTimelineV63(tab, timelineRow + 1, window, states, StateBrush);
            var nextRow = timelineRow + 3 +
                Math.Max(1, states.Select(s => s.WorkcenterCode).Distinct().Count());
            XlSectionV63(tab, nextRow, "Procesní hodnoty - časová osa MES06", 5);
            nextRow = ExcelProcessTimelineV63(tab, nextRow + 1, window, messages);
            nextRow += 2;
            XlSectionV63(tab, nextRow, "Stavy a prostoje", 5);
            nextRow = XlTableV63(tab, nextRow + 1, new[] { "Stav", "Minut" }, StateSummaryRowsV63(states, new[] { window }));
            XlSectionV63(tab, nextRow + 2, "Operátoři - skutečné přihlášení a odhlášení", 5);
            nextRow = XlTableV63(tab, nextRow + 3,
                new[] { "Pracoviště", "Osobní číslo", "Jméno", "Přihlášení", "Odhlášení", "Hodin ve směně" },
                operators.Select(o => new object?[] { o.Workcenter, o.Code, o.Name, o.Login,
                    (object?)(o.Logout?.ToString("dd.MM.yyyy HH:mm") ?? MesordText("Otevřeno")), Math.Round(o.HoursInWindow, 2) }));
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
        foreach (var stream in retainedStreams) stream.Dispose();
    }
}
