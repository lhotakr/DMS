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
    private static void WriteImagePdfV6(string path, IReadOnlyList<byte[]> jpegPages, int width, int height)
    {
        // Minimal standards-compliant PDF 1.4 writer, inspired by MES06 print export.
        var numberOfObjects = 2 + 3 * jpegPages.Count;
        var offsets = new long[numberOfObjects + 1];
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        void Ascii(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            file.Write(bytes, 0, bytes.Length);
        }
        void BeginObject(int id) { offsets[id] = file.Position; Ascii($"{id} 0 obj\n"); }
        void EndObject() { Ascii("\nendobj\n"); }
        Ascii("%PDF-1.4\n");
        BeginObject(1); Ascii("<< /Type /Catalog /Pages 2 0 R >>"); EndObject();
        BeginObject(2);
        Ascii("<< /Type /Pages /Count " + jpegPages.Count + " /Kids [" +
            string.Join(" ", Enumerable.Range(0, jpegPages.Count).Select(i => (3 + 3 * i) + " 0 R")) + "] >>");
        EndObject();
        for (var i = 0; i < jpegPages.Count; i++)
        {
            var pageId = 3 + 3 * i;
            var contentsId = pageId + 1;
            var imageId = pageId + 2;
            BeginObject(pageId);
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentsId} 0 R >>");
            EndObject();
            var instructions = Encoding.ASCII.GetBytes("q\n595 0 0 842 0 0 cm\n/Im0 Do\nQ\n");
            BeginObject(contentsId);
            Ascii($"<< /Length {instructions.Length} >>\nstream\n");
            file.Write(instructions, 0, instructions.Length);
            Ascii("endstream"); EndObject();
            var bytes = jpegPages[i];
            BeginObject(imageId);
            Ascii($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {bytes.Length} >>\nstream\n");
            file.Write(bytes, 0, bytes.Length);
            Ascii("\nendstream"); EndObject();
        }
        var xrefStart = file.Position;
        Ascii($"xref\n0 {numberOfObjects + 1}\n0000000000 65535 f \n");
        for (var id = 1; id <= numberOfObjects; id++) Ascii($"{offsets[id]:0000000000} 00000 n \n");
        Ascii($"trailer\n<< /Size {numberOfObjects + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");
    }

    private TextBlock PrintTextV63(string value, int size = 11, bool strong = false) => new()
    {
        Text = MesordText(value), FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3)
    };


    private System.Windows.Documents.Table PdfTableV63(string[] headers,
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
        AddRow(headers.Select(MesordText).ToArray(), true);
        var count = 0;
        foreach (var row in rows) { AddRow(row, false); count++; }
        if (count == 0) AddRow(new[] { MesordText("Bez záznamů") }, false);
        return table;
    }


    private Paragraph PdfTitleV63(string text, int size = 15) => new(new Bold(new Run(MesordText(text))))
    { FontSize = size, Foreground = Brushes.Black, Margin = new Thickness(0, 11, 0, 5) };


    private UIElement PrintStatePieV63(IReadOnlyList<MesStateRecord> states,
        IReadOnlyList<MesordV6ShiftWindow> windows)
    {
        var values = states.GroupBy(s => s.StateName)
            .Select(g => new { Name = g.Key ?? "", Minutes = StateMinutesV62(g, windows), Color = StateBrush(g.First()) })
            .Where(g => g.Minutes > 0).OrderByDescending(g => g.Minutes).ToList();
        var root = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };
        if (values.Count == 0) { root.Children.Add(PrintTextV63(MesordText("Bez záznamů stavů."))); return root; }
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


    private Canvas PrintProcessTimelineV63(MesordV6ShiftWindow window,
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
        doc.Blocks.Add(new Paragraph(new Run($"{MesordText("Vytvořeno")}: {DateTime.Now:dd.MM.yyyy HH:mm}    {MesordText("Autor (Windows)")}: {AuthorV63}"))
            { FontSize = 10, Margin = new Thickness(0, 0, 0, 10), Foreground = Brushes.Black });
        doc.Blocks.Add(PdfTitleV63("Historie výrobní zakázky", 21));
        doc.Blocks.Add(new Paragraph(new Run($"{MesordText("Závod 2000")}: {_orderNumber}    |    {MesordText("Závod 9200")}: {MesordText(Plant9200V63)}"))
            { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black });
        doc.Blocks.Add(new Paragraph(new Run($"{MesordText("Průběh zakázky")}: {OrderDatesV63(_analyticsFrom, _analyticsTo)}"))
            { Margin = new Thickness(0, 4, 0, 4), Foreground = Brushes.Black });
        if (_mesordExportOrderV63 is { } order)
        {
            doc.Blocks.Add(new Paragraph(new Run($"{MesordText("Artikl")}: {order.ProductCode}  |  SAP: {order.SapArticleNumber}"))
                { Foreground = Brushes.Black });
            doc.Blocks.Add(new Paragraph(new Run(
                $"{MesordText("Plán")}: {order.TargetQuantity:N0}    OK: {order.FinishedQuantity:N0}    NOK: {order.ScrapQuantity:N0}"))
                { Foreground = Brushes.Black });
        }
        doc.Blocks.Add(PdfTitleV63("Souhrn zakázky"));
        doc.Blocks.Add(new BlockUIContainer(PrintStatePieV63(data.States, WholeOrderWindow()))
            { Margin = new Thickness(0, 4, 0, 4) });
        doc.Blocks.Add(PdfTitleV63("Stavy a prostoje", 12));
        doc.Blocks.Add(PdfTableV63(new[] { "Stav", "Minut" },
            StateSummaryRowsV63(data.States, WholeOrderWindow())
                .Select(row => row.Select(value => Convert.ToString(value, MesordCsV63) ?? "").ToArray())));
        doc.Blocks.Add(PdfTitleV63("Čítače - součet uložených hodnot", 12));
        doc.Blocks.Add(PdfTableV63(new[] { "Pracoviště", "Čítač", "Součet hodnot" },
            CounterRowsV63(data.Counters).Select(r => r.Select(x => Convert.ToString(x, MesordCsV63) ?? "").ToArray())));
        doc.Blocks.Add(new Paragraph(new Run(
            MesordText("Pozn.: součet hodnot kumulativního čítače není automaticky počtem vyrobených kusů.")))
            { FontSize = 9, Foreground = Brushes.Black, Margin = new Thickness(0, 2, 0, 10) });
        foreach (var window in data.Windows.OrderBy(w => w.Start))
        {
            var states = data.States.Where(s => Overlap(s.Starttime, s.Endtime ?? _analyticsTo,
                window.Start, window.End)).ToList();
            var counters = data.Counters.Where(c => c.Timestamp >= window.Start && c.Timestamp < window.End).ToList();
            var operators = OperatorSessionsV63(data.Operators, new[] { window });
            var messages = data.Messages.Where(m => Overlap(m.Starttime, m.Endtime, window.Start, window.End)).ToList();
            var section = new Section { BreakPageBefore = true };
            section.Blocks.Add(PdfTitleV63($"{window.ShiftDay:dd.MM.yyyy} - {MesordText(window.Name)}", 17));
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
                    o.Login.ToString("dd.MM HH:mm"), o.Logout?.ToString("dd.MM HH:mm") ?? MesordText("Otevřeno"),
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
            const int renderDpi = 240;
            const double scale = renderDpi / 96d;
            var bitmap = new RenderTargetBitmap((int)Math.Round(pageWidth * scale),
                (int)Math.Round(pageHeight * scale), renderDpi, renderDpi, PixelFormats.Pbgra32);
            bitmap.Render(target);
            var encoder = new JpegBitmapEncoder { QualityLevel = 94 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            images.Add(stream.ToArray());
        }
        if (images.Count == 0) throw new InvalidOperationException(MesordText("PDF nemá žádnou vykreslitelnou stránku."));
        WriteImagePdfV6(path, images, (int)Math.Round(pageWidth * 240d / 96d),
            (int)Math.Round(pageHeight * 240d / 96d));
    }
}
