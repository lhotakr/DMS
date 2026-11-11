using ClosedXML.Excel;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DMS.Desktop.Views.Orders;

/// <summary>
/// MESORD v6 – shift-day aggregation, MES06-style process timelines, exports.
/// Read-only: never modifies FASTEC/SAP records.
/// </summary>
public sealed partial class MesOrderHistoryView
{
    private sealed class MesordV6ShiftWindow
    {
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class MesordV6Data
    {
        public List<MesordV6ShiftWindow> Windows { get; init; } = new();
        public List<MesStateRecord> States { get; init; } = new();
        public List<MesCounterRecord> Counters { get; init; } = new();
        public List<MesLoggedOperatorRecord> Operators { get; init; } = new();
        public List<Mes06ProcessValueRecord> Messages { get; init; } = new();
    }

    private static readonly string[] MesordV6ShiftNames = { "Ranní", "Odpolední", "Noční" };

    private List<MesordV6ShiftWindow> GetShiftWindowsV6()
    {
        var result = new List<MesordV6ShiftWindow>();
        if (_analyticsTo <= _analyticsFrom) return result;
        var earliest = _selectedDay?.Date.AddHours(5).AddMinutes(55);
        var latest = earliest?.AddDays(1);
        // Bound includes the shift that started before the query and crosses midnight.
        for (var start = ShiftBeginning(_analyticsFrom); start < _analyticsTo; start = start.AddHours(8))
        {
            if (earliest.HasValue && (start < earliest.Value || start >= latest!.Value)) continue;
            var a = start > _analyticsFrom ? start : _analyticsFrom;
            var b = start.AddHours(8) < _analyticsTo ? start.AddHours(8) : _analyticsTo;
            if (b <= a) continue;
            result.Add(new MesordV6ShiftWindow { Start = a, End = b, Name = ShiftLabel(start) });
        }
        return result;
    }

    private MesordV6Data BuildDataV6(OperationChoice? operation)
    {
        var intervals = _allProduction.Where(p => operation == null || string.IsNullOrEmpty(operation.Code)
            || (p.OperationCode == operation.Code && p.WorkcenterCode == operation.Workcenter)).ToList();
        bool SelectedWorkcenter(string wc) => operation == null || string.IsNullOrEmpty(operation.Code)
            || string.Equals(wc, operation.Workcenter, StringComparison.OrdinalIgnoreCase);
        bool FitsOperation(string wc, DateTime time) =>
            operation == null || string.IsNullOrEmpty(operation.Code) ||
            (SelectedWorkcenter(wc) && intervals.Any(p => p.Starttime <= time && (p.Endtime ?? _analyticsTo) > time));
        return new MesordV6Data
        {
            Windows = GetShiftWindowsV6(),
            States = _allStates.Where(IsRelevant).Where(s =>
                operation == null || string.IsNullOrEmpty(operation.Code) ||
                (SelectedWorkcenter(s.WorkcenterCode) && intervals.Any(p =>
                    Overlap(s.Starttime, s.Endtime ?? _analyticsTo, p.Starttime, p.Endtime ?? _analyticsTo)))).ToList(),
            Counters = _allCounters.Where(c => FitsOperation(c.WorkcenterCode, c.Timestamp)).ToList(),
            Operators = _allOperators.Where(o => o.HasOperator &&
                string.Equals(o.OrderCode, _orderNumber, StringComparison.OrdinalIgnoreCase) &&
                (operation == null || string.IsNullOrEmpty(operation.Code) ||
                 (o.OperationCode == operation.Code && SelectedWorkcenter(o.WorkcenterCode)))).ToList(),
            Messages = _processMessages.Where(m =>
                string.Equals(m.OrderCode, _orderNumber, StringComparison.OrdinalIgnoreCase) &&
                (operation == null || string.IsNullOrEmpty(operation.Code) ||
                 (m.OperationCode == operation.Code && SelectedWorkcenter(m.WorkcenterCode)))).ToList()
        };
    }

    private static bool InWindowsV6(DateTime moment, IReadOnlyList<MesordV6ShiftWindow> windows) =>
        windows.Any(w => moment >= w.Start && moment < w.End);

    private static bool OverlapsWindowsV6(DateTime start, DateTime end, IReadOnlyList<MesordV6ShiftWindow> windows) =>
        windows.Any(w => Overlap(start, end, w.Start, w.End));

    private static double MinutesInWindowsV6(DateTime from, DateTime to,
        IReadOnlyList<MesordV6ShiftWindow> windows) =>
        windows.Sum(w => Minutes(from, to, w.Start, w.End));

    private void AddExportControlsV6()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var excel = new Button { Content = "Exportovat XLSX", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var pdf = new Button { Content = "Exportovat PDF", Padding = new Thickness(12, 6, 12, 6) };
        excel.Click += (_, _) => ExportMesordV6(false);
        pdf.Click += (_, _) => ExportMesordV6(true);
        panel.Children.Add(excel);
        panel.Children.Add(pdf);
        _analyticsPanel.Children.Add(panel);
    }

    private void RenderShiftTabsV6()
    {
        var data = BuildDataV6(_operationSelector.SelectedItem as OperationChoice);
        if (_selectedDay.HasValue)
            _analyticsPanel.Children.Add(Label("Směnový den: " + _selectedDay.Value.ToString("dd.MM.yyyy") + " (05:55–05:55 následujícího dne)"));
        else
            _analyticsPanel.Children.Add(Label("Všechny směnové dny zakázky – sloučeno podle typu směny"));

        var tabs = new TabControl { Margin = new Thickness(0, 7, 0, 12) };
        foreach (var name in MesordV6ShiftNames)
        {
            var windows = data.Windows.Where(w => w.Name == name).ToList();
            var states = data.States.Where(s => OverlapsWindowsV6(s.Starttime, s.Endtime ?? _analyticsTo, windows)).ToList();
            var counters = data.Counters.Where(c => InWindowsV6(c.Timestamp, windows)).ToList();
            var operators = data.Operators.Where(o => o.LoginFrom.HasValue &&
                OverlapsWindowsV6(o.LoginFrom.Value, o.LoginTo ?? _analyticsTo, windows)).ToList();
            var messages = data.Messages.Where(m => OverlapsWindowsV6(m.Starttime, m.Endtime, windows)).ToList();

            var content = new StackPanel { Margin = new Thickness(8) };
            content.Children.Add(Label($"{name} · {windows.Count} směn · stavy {states.Count} · operátoři {operators.Count} · čítače {counters.Count}", 14));

            // Every row denotes a distinct workcenter + process state, as in MES06.
            content.Children.Add(Label("Procesní hodnoty – časová osa MES06", 16));
            var processStack = new StackPanel();
            foreach (var window in windows)
            {
                var windowMessages = messages.Where(m => Overlap(m.Starttime, m.Endtime, window.Start, window.End))
                    .OrderBy(m => m.WorkcenterCode).ThenBy(m => m.StateName).ThenBy(m => m.Starttime).ToList();
                if (windowMessages.Count == 0) continue;
                var expander = new Expander
                {
                    Header = $"{window.Start:dd.MM.yyyy} · {window.Start:HH:mm}–{window.End:HH:mm} · {windowMessages.Count} záznamů",
                    Margin = new Thickness(0, 3, 0, 3)
                };
                // Create only expanded charts: historical orders can span hundreds of shifts.
                if (windows.Count <= 2)
                {
                    expander.Content = CreateProcessTimelineV6(windowMessages, window.Start, window.End);
                    expander.IsExpanded = true;
                }
                else
                {
                    expander.Expanded += (_, _) =>
                    {
                        if (expander.Content == null)
                            expander.Content = CreateProcessTimelineV6(windowMessages, window.Start, window.End);
                    };
                }
                processStack.Children.Add(expander);
            }
            if (processStack.Children.Count == 0)
                content.Children.Add(Label("Pro tuto směnu nejsou historické procesní záznamy."));
            else content.Children.Add(processStack);

            // Exactly three summary tables; avoid stretching narrow tables to full-page width.
            var summary = new Grid { MinWidth = 960, Margin = new Thickness(0, 6, 0, 10) };
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var totalStateMinutes = states.Sum(s => MinutesInWindowsV6(s.Starttime, s.Endtime ?? _analyticsTo, windows));
            var stateRows = states.GroupBy(s => s.StateName)
                .Select(g => new
                {
                    Stav = g.Key,
                    Minut = Math.Round(g.Sum(s => MinutesInWindowsV6(s.Starttime, s.Endtime ?? _analyticsTo, windows)), 1),
                    PodílProcent = Math.Round(100 * g.Sum(s => MinutesInWindowsV6(s.Starttime, s.Endtime ?? _analyticsTo, windows)) / Math.Max(1, totalStateMinutes), 1)
                }).OrderByDescending(x => x.Minut).Cast<object>();
            var operatorRows = operators.GroupBy(o => new { o.HumanCode, o.OperatorName })
                .Select(g => new
                {
                    OsobníČíslo = g.Key.HumanCode,
                    Jméno = g.Key.OperatorName,
                    Hodin = Math.Round(windows.Sum(w => UnionMinutes(
                        g.Where(o => o.LoginFrom.HasValue).Select(o =>
                            (o.LoginFrom!.Value, o.LoginTo ?? _analyticsTo)), w.Start, w.End)) / 60, 2)
                }).OrderBy(x => x.OsobníČíslo).Cast<object>();
            var counterRows = counters.GroupBy(c => new { c.WorkcenterCode, c.CounterName })
                .Select(g => new
                {
                    Pracoviště = g.Key.WorkcenterCode, Čítač = g.Key.CounterName,
                    PosledníHodnota = Math.Round(g.OrderByDescending(c => c.Timestamp).First().Value, 0)
                }).OrderBy(x => x.Pracoviště).ThenBy(x => x.Čítač).Cast<object>();
            AddSummaryColumnV6(summary, 0, "Stavy", stateRows);
            AddSummaryColumnV6(summary, 1, "Operátoři", operatorRows);
            AddSummaryColumnV6(summary, 2, "Čítače (poslední hodnota)", counterRows);
            content.Children.Add(new ScrollViewer { Content = summary, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });

            var tab = new TabItem { Header = name, Content = new ScrollViewer {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 850 } };
            tabs.Items.Add(tab);
        }
        _analyticsPanel.Children.Add(tabs);
    }

    private static void AddSummaryColumnV6(Grid grid, int column, string title, IEnumerable<object> values)
    {
        var holder = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0) };
        holder.Children.Add(Label(title, 16));
        holder.Children.Add(Table(values));
        Grid.SetColumn(holder, column);
        grid.Children.Add(holder);
    }

    private static UIElement CreateProcessTimelineV6(
        IReadOnlyList<Mes06ProcessValueRecord> messages, DateTime start, DateTime end)
    {
        const double chartWidth = 1120;
        const double left = 240;
        const double plotWidth = chartWidth - left - 20;
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

    private sealed class MesordV6ExportTable
    {
        public string Name { get; init; } = "";
        public string[] Headers { get; init; } = Array.Empty<string>();
        public List<object?[]> Rows { get; } = new();
    }

    private List<MesordV6ExportTable> PrepareExportV6(MesordV6Data data)
    {
        var shiftOverview = new MesordV6ExportTable { Name = "Souhrn směn", Headers = new[] {
            "Směna", "Datum", "Od", "Do", "Stavy (min)", "Operátoři", "Čítače", "Procesní hodnoty" } };
        var states = new MesordV6ExportTable { Name = "Stavy", Headers = new[] {
            "Směna", "Začátek", "Konec", "Pracoviště", "Stav", "Minut" } };
        var operators = new MesordV6ExportTable { Name = "Operátoři", Headers = new[] {
            "Směna", "Od", "Do", "Pracoviště", "Osobní číslo", "Jméno", "Hodin" } };
        var counters = new MesordV6ExportTable { Name = "Čítače", Headers = new[] {
            "Směna", "Čas", "Pracoviště", "Čítač", "Hodnota" } };
        var messages = new MesordV6ExportTable { Name = "Procesní hodnoty", Headers = new[] {
            "Směna", "Od", "Do", "Pracoviště", "Stav", "Úroveň", "Zakázka", "Artikl", "Operace" } };
        foreach (var w in data.Windows)
        {
            var ss = data.States.Where(s => Overlap(s.Starttime, s.Endtime ?? _analyticsTo, w.Start, w.End)).ToList();
            var oo = data.Operators.Where(o => o.LoginFrom.HasValue &&
                Overlap(o.LoginFrom.Value, o.LoginTo ?? _analyticsTo, w.Start, w.End)).ToList();
            var cc = data.Counters.Where(c => c.Timestamp >= w.Start && c.Timestamp < w.End).ToList();
            var mm = data.Messages.Where(m => Overlap(m.Starttime, m.Endtime, w.Start, w.End)).ToList();
            shiftOverview.Rows.Add(new object?[] { w.Name, w.Start.Date, w.Start, w.End,
                Math.Round(ss.Sum(s => Minutes(s.Starttime, s.Endtime ?? _analyticsTo, w.Start, w.End)), 1),
                oo.Select(o => o.HumanCode).Distinct().Count(), cc.Count, mm.Count });
            foreach (var s in ss)
            {
                var a = s.Starttime > w.Start ? s.Starttime : w.Start;
                var b = (s.Endtime ?? _analyticsTo) < w.End ? (s.Endtime ?? _analyticsTo) : w.End;
                states.Rows.Add(new object?[] { w.Name, a, b, s.WorkcenterCode, s.StateName, Math.Round((b-a).TotalMinutes, 2) });
            }
            foreach (var o in oo)
            {
                var a = o.LoginFrom!.Value > w.Start ? o.LoginFrom.Value : w.Start;
                var b = (o.LoginTo ?? _analyticsTo) < w.End ? (o.LoginTo ?? _analyticsTo) : w.End;
                operators.Rows.Add(new object?[] { w.Name, a, b, o.WorkcenterCode, o.HumanCode, o.OperatorName,
                    Math.Round((b-a).TotalHours, 2) });
            }
            foreach (var c in cc)
                counters.Rows.Add(new object?[] { w.Name, c.Timestamp, c.WorkcenterCode, c.CounterName, Math.Round(c.Value, 2) });
            foreach (var m in mm)
            {
                var a = m.Starttime > w.Start ? m.Starttime : w.Start;
                var b = m.Endtime < w.End ? m.Endtime : w.End;
                messages.Rows.Add(new object?[] { w.Name, a, b, m.WorkcenterCode, m.StateName, m.Level,
                    m.OrderCode, m.ProductCode, m.OperationCode });
            }
        }
        return new List<MesordV6ExportTable> { shiftOverview, states, operators, counters, messages };
    }

    private void ExportMesordV6(bool pdf)
    {
        if (!_dataLoaded) return;
        var fileType = pdf ? "PDF" : "XLSX";
        var ext = pdf ? ".pdf" : ".xlsx";
        var dialog = new SaveFileDialog { DefaultExt = ext,
            Filter = pdf ? "PDF dokument (*.pdf)|*.pdf" : "Excel (*.xlsx)|*.xlsx",
            FileName = $"MESORD_{_orderNumber}_{DateTime.Now:yyyyMMdd-HHmm}{ext}" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var data = BuildDataV6(_operationSelector.SelectedItem as OperationChoice);
            var tables = PrepareExportV6(data);
            var operation = _operationSelector.SelectedItem as OperationChoice;
            var meta = new[] {
                "Zakázka: " + _orderNumber,
                "Operace: " + (operation?.Caption ?? "Celá zakázka"),
                "Období zakázky: " + _analyticsFrom.ToString("dd.MM.yyyy HH:mm") + "–" + _analyticsTo.ToString("dd.MM.yyyy HH:mm"),
                "Směnový den: " + (_selectedDay?.ToString("dd.MM.yyyy") ?? "všechny směnové dny"),
                "Směny: 05:55–13:55 / 13:55–21:55 / 21:55–05:55",
                "Čítače: historické zaznamenané hodnoty, bez automatického součtu.",
                "Procesní hodnoty: historické stavové intervaly MES06; nikoliv naměřené teploty.",
                "Vytvořeno: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
            };
            if (pdf) WritePdfV6(dialog.FileName, meta, tables);
            else WriteExcelV6(dialog.FileName, meta, tables);
            _logger.Info($"MESORD Export{fileType}: Order={_orderNumber}; Operation={operation?.Code}; ShiftDay={_selectedDay:yyyy-MM-dd}; File={dialog.FileName}");
            MessageBox.Show($"Export {fileType} byl vytvořen:\n{dialog.FileName}", "MESORD", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error("MESORD export failed.", ex);
            MessageBox.Show("Export se nepodařil:\n" + ex.Message, "MESORD", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void WriteExcelV6(string path, IReadOnlyList<string> meta, IEnumerable<MesordV6ExportTable> tables)
    {
        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Zakázka");
        summary.Cell(1, 1).Value = "MESORD – historie výrobní zakázky";
        summary.Cell(1, 1).Style.Font.Bold = true;
        for (var i = 0; i < meta.Count; i++) summary.Cell(i + 3, 1).Value = meta[i];
        summary.Column(1).Width = 96;
        foreach (var table in tables)
        {
            var ws = workbook.Worksheets.Add(table.Name);
            for (var c = 0; c < table.Headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = table.Headers[c];
                ws.Cell(1, c + 1).Style.Font.Bold = true;
            }
            var row = 2;
            foreach (var values in table.Rows)
            {
                for (var c = 0; c < values.Length; c++)
                {
                    var cell = ws.Cell(row, c + 1);
                    switch (values[c])
                    {
                        case DateTime date:
                            cell.Value = date;
                            cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
                            break;
                        case int count: cell.Value = count; break;
                        case double number: cell.Value = number; break;
                        case decimal amount: cell.Value = amount; break;
                        case null: cell.Value = ""; break;
                        default: cell.Value = Convert.ToString(values[c], CultureInfo.CurrentCulture) ?? ""; break;
                    }
                }
                row++;
            }
            ws.SheetView.FreezeRows(1);
            ws.ColumnsUsed().AdjustToContents();
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        }
        workbook.SaveAs(path);
    }

    private static void WritePdfV6(string path, IReadOnlyList<string> meta, IEnumerable<MesordV6ExportTable> tables)
    {
        const double pageWidth = 793; const double pageHeight = 1122;
        var doc = new FlowDocument { PageWidth = pageWidth, PageHeight = pageHeight,
            PagePadding = new Thickness(38), ColumnWidth = pageWidth - 76,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 10 };
        doc.Blocks.Add(new Paragraph(new Bold(new Run("MESORD – historie výrobní zakázky"))) { FontSize = 20 });
        foreach (var line in meta)
            doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 1, 0, 2) });
        foreach (var table in tables)
        {
            doc.Blocks.Add(new Paragraph(new Bold(new Run(table.Name + " (" + table.Rows.Count + ")"))) {
                FontSize = 15, Margin = new Thickness(0, 14, 0, 4) });
            // Readable wrapped rows instead of truncating columns on A4.
            foreach (var values in table.Rows)
            {
                var details = string.Join("  |  ", table.Headers.Zip(values, (header, value) =>
                    header + ": " + (value is DateTime d ? d.ToString("dd.MM.yyyy HH:mm") : Convert.ToString(value, CultureInfo.CurrentCulture))));
                doc.Blocks.Add(new Paragraph(new Run(details)) { Margin = new Thickness(0, 1, 0, 2),
                    BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.3) });
            }
            if (table.Rows.Count == 0) doc.Blocks.Add(new Paragraph(new Run("Bez záznamů.")));
        }
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.PageSize = new Size(pageWidth, pageHeight);
        paginator.ComputePageCount();
        var jpgPages = new List<byte[]>();
        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = paginator.GetPage(i);
            if (page == null || ReferenceEquals(page,DocumentPage.Missing)) continue;
            var target = new DrawingVisual();
            using (var context = target.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));
                context.DrawRectangle(new VisualBrush(page.Visual), null, new Rect(0, 0, pageWidth, pageHeight));
            }
            var bitmap = new RenderTargetBitmap((int)pageWidth, (int)pageHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(target);
            var jpeg = new JpegBitmapEncoder { QualityLevel = 85 };
            jpeg.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            jpeg.Save(stream);
            jpgPages.Add(stream.ToArray());
        }
        if (jpgPages.Count == 0) throw new InvalidOperationException("PDF: nebyla vytvořena žádná stránka.");
        WriteImagePdfV6(path, jpgPages, (int)pageWidth, (int)pageHeight);
    }

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
}
