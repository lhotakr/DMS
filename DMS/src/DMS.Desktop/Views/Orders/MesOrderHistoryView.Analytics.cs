using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Models;
using DMS.Integration.Mes.Orders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Orders;

public sealed partial class MesOrderHistoryView
{
    private readonly StackPanel _analyticsPanel = new() { Margin = new Thickness(0, 18, 0, 0) };
    private readonly List<MesStateRecord> _allStates = new();
    private readonly List<MesCounterRecord> _allCounters = new();
    private readonly List<MesProductionRecord> _allProduction = new();
    private readonly List<MesLoggedOperatorRecord> _allOperators = new();
    private readonly List<MesReportingStateColor> _stateColors = new();
    private readonly List<Mes06ProcessValueRecord> _processMessages = new();
    private readonly List<string> _analyticsErrors = new();
    private readonly ComboBox _operationSelector = new() { MinWidth = 220, Margin = new Thickness(8, 0, 0, 0) };
    private readonly System.Windows.Controls.Calendar _calendar = new() { SelectionMode = CalendarSelectionMode.SingleDate,
        Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
    private DateTime _analyticsFrom, _analyticsTo;
    private DateTime? _selectedDay;
    private bool _dataLoaded;

    private sealed class OperationChoice
    {
        public string Code { get; init; } = "";
        public string Workcenter { get; init; } = "";
        public string Caption { get; init; } = "";
        public override string ToString() => Caption;
    }

    private static DateTime ShiftBeginning(DateTime time)
    {
        var day = time.Date;
        if (time.TimeOfDay < new TimeSpan(5,55,0)) return day.AddDays(-1).AddHours(21).AddMinutes(55);
        if (time.TimeOfDay < new TimeSpan(13,55,0)) return day.AddHours(5).AddMinutes(55);
        if (time.TimeOfDay < new TimeSpan(21,55,0)) return day.AddHours(13).AddMinutes(55);
        return day.AddHours(21).AddMinutes(55);
    }
    private static string ShiftLabel(DateTime start) => start.Hour == 5 ? "Ranní" : start.Hour == 13 ? "Odpolední" : "Noční";
    private static TextBlock Label(string text, int size = 14) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 5)
    };
    private static Border Card(UIElement content, Thickness? margin = null) => new()
    {
        Child = content, Padding = new Thickness(12),
        Margin = margin ?? new Thickness(0, 4, 0, 8),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromRgb(92, 103, 118))
    };
    private static DataGrid Table(IEnumerable<object> rows) => new()
    {
        ItemsSource = rows.ToList(), AutoGenerateColumns = true,
        IsReadOnly = true, CanUserAddRows = false,
        MinHeight = 90, MaxHeight = 280, Margin = new Thickness(0, 3, 0, 12)
    };
    private static Color? ParseFastecColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var converted = ColorConverter.ConvertFromString(raw.Trim());
            if (converted is Color c) return c;
        }
        catch { /* Some FASTEC colors are stored as numeric components. */ }
        var parts = raw.Split(new[] {',',';',' '}, StringSplitOptions.RemoveEmptyEntries);
        if ((parts.Length == 3 || parts.Length == 4) &&
            parts.All(x => byte.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var values = parts.Select(x => byte.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            return values.Length == 3 ? Color.FromRgb(values[0],values[1],values[2])
                : Color.FromArgb(values[0],values[1],values[2],values[3]);
        }
        return null;
    }
    private Brush StateBrush(MesStateRecord state)
    {
        var definition = _stateColors.FirstOrDefault(x =>
            string.Equals(x.WorkcenterCode,state.WorkcenterCode,StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.StateName,state.StateName,StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.CategoryName,state.CategoryName,StringComparison.OrdinalIgnoreCase))
            ?? _stateColors.FirstOrDefault(x =>
                string.Equals(x.WorkcenterCode,state.WorkcenterCode,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.StateName,state.StateName,StringComparison.OrdinalIgnoreCase))
            ?? _stateColors.FirstOrDefault(x =>
                string.IsNullOrEmpty(x.WorkcenterCode) &&
                string.Equals(x.StateName,state.StateName,StringComparison.OrdinalIgnoreCase));
        var color = ParseFastecColor(definition?.StateColor ?? "")
            ?? ParseFastecColor(definition?.CategoryColor ?? "")
            ?? Color.FromRgb(130,141,153);
        return new SolidColorBrush(color);
    }
    private static bool IsRelevant(MesStateRecord state) =>
        !string.Equals((state.StateName ?? "").Trim(), "Náhradní stroj", StringComparison.OrdinalIgnoreCase)
        && !string.Equals((state.CategoryName ?? "").Trim(), "Náhradní stroj", StringComparison.OrdinalIgnoreCase);
    private static string UnitHours(double minutes) => (minutes / 60d).ToString("N2", new CultureInfo("cs-CZ")) + " h";
    private static double UnionMinutes(IEnumerable<(DateTime from, DateTime to)> raw, DateTime from, DateTime to)
    {
        var ranges = raw.Select(r => (a: r.from > from ? r.from : from, b: r.to < to ? r.to : to))
            .Where(r => r.b > r.a).OrderBy(r=>r.a).ToList();
        if (ranges.Count == 0) return 0;
        double minutes = 0;
        var a = ranges[0].a; var b = ranges[0].b;
        foreach (var r in ranges.Skip(1))
        {
            if (r.a <= b) { if (r.b > b) b = r.b; }
            else { minutes += (b-a).TotalMinutes; a = r.a; b = r.b; }
        }
        return minutes + (b-a).TotalMinutes;
    }
    private static Border Swatch(Brush fill, string tooltip = "") => new()
    {
        Width=13, Height=13, Margin=new Thickness(0,0,7,0), Background=fill,
        CornerRadius=new CornerRadius(3), ToolTip=tooltip
    };
    private UIElement PieChart(IReadOnlyList<MesStateRecord> states, DateTime start, DateTime end)
    {
        var items = states.GroupBy(x=>x.StateName).Select(g=>new
        {
            Name=g.Key, Value=g.Sum(x=>Minutes(x.Starttime,x.Endtime ?? _analyticsTo,start,end)),
            Color=StateBrush(g.First())
        }).Where(x=>x.Value>0).OrderByDescending(x=>x.Value).ToList();
        if (items.Count == 0) return Label("Pro tuto směnu nejsou dostupné záznamy stavů.");
        var layout = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,7,0,12) };
        var pie = new Canvas { Width=188, Height=188, Margin=new Thickness(0,0,20,0) };
        double sum=items.Sum(x=>x.Value), angle=-90d;
        foreach (var item in items)
        {
            var sweep=360*item.Value/sum;
            var startAngle=angle*Math.PI/180;
            var endAngle=(angle+sweep)*Math.PI/180;
            var center=new Point(94,94);
            // Full circle must be rendered as ellipse, not a 360° ArcSegment.
            if (sweep > 359.99)
            {
                pie.Children.Add(new System.Windows.Shapes.Ellipse {
                    Width=180,Height=180,Fill=item.Color,ToolTip=item.Name+" · "+UnitHours(item.Value)
                });
                break;
            }
            var p1=new Point(94+90*Math.Cos(startAngle),94+90*Math.Sin(startAngle));
            var p2=new Point(94+90*Math.Cos(endAngle),94+90*Math.Sin(endAngle));
            var figure=new PathFigure { StartPoint=center,IsClosed=true,IsFilled=true };
            figure.Segments.Add(new LineSegment(p1,true));
            figure.Segments.Add(new ArcSegment(p2,new Size(90,90),0,sweep>180,SweepDirection.Clockwise,true));
            figure.Segments.Add(new LineSegment(center,true));
            pie.Children.Add(new System.Windows.Shapes.Path {
                Data=new PathGeometry(new[] { figure }),Fill=item.Color,
                ToolTip=item.Name+" · "+UnitHours(item.Value)+" · "+(100*item.Value/sum).ToString("N1")+" %"
            });
            angle+=sweep;
        }
        layout.Children.Add(pie);
        var legend=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        foreach (var item in items)
        {
            var line=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,3,0,3) };
            line.Children.Add(Swatch(item.Color));
            line.Children.Add(Label(item.Name+" — "+UnitHours(item.Value)+" ("+(100*item.Value/sum).ToString("N1")+" %)",12));
            legend.Children.Add(line);
        }
        layout.Children.Add(legend);
        return layout;
    }
    private static bool Overlap(DateTime start, DateTime end, DateTime from, DateTime to) =>
        start < to && end > from;
    private static double Minutes(DateTime start, DateTime end, DateTime from, DateTime to) =>
        Math.Max(0, ( (end < to ? end : to) - (start > from ? start : from)).TotalMinutes);

    private async Task LoadAnalyticsAsync(MesDatabaseConnectionSettings settings, DateTime? actualStart, DateTime? actualEnd)
    {
        _analyticsPanel.Children.Clear();
        _analyticsPanel.Children.Add(Label("Analytika zakázky", 20));
        if (!actualStart.HasValue)
        {
            _analyticsPanel.Children.Add(Label("Chybí skutečný začátek zakázky."));
            return;
        }
        _analyticsFrom = actualStart.Value;
        _analyticsTo = actualEnd ?? DateTime.Now;
        if (_analyticsTo <= _analyticsFrom) { _analyticsPanel.Children.Add(Label("Neplatné období zakázky.")); return; }

        var reporting = new MesReportingDataService(settings);
        var enrichment = new MesReportingEnrichmentService(settings);
        _allStates.Clear(); _allCounters.Clear(); _allProduction.Clear(); _allOperators.Clear();
        _stateColors.Clear(); _processMessages.Clear(); _analyticsErrors.Clear();
        try { _stateColors.AddRange(await enrichment.GetStateColorsAsync()); }
        catch (Exception ex) { _analyticsErrors.Add("Barvy stavů: " + ex.Message); }
        _dataLoaded = false;
        _analyticsPanel.Children.Add(Label("Načítám výrobní historii, čítače a operátory…"));
        // Bounded windows avoid one huge report. Every request is read-only.
        for (var start = _analyticsFrom; start < _analyticsTo; start = start.AddDays(1))
        {
            var end = start.AddDays(1) < _analyticsTo ? start.AddDays(1) : _analyticsTo;
            var filter = new MesReportFilter { From = start, To = end, OrderCode = _orderNumber, MaxRows = 50000 };
            try { _allStates.AddRange(await reporting.GetStatesAsync(filter)); }
            catch (Exception ex) { _analyticsErrors.Add("Stavy: " + ex.Message); }
            try { _allCounters.AddRange(await reporting.GetCountersAsync(filter)); }
            catch (Exception ex) { _analyticsErrors.Add("Čítače: " + ex.Message); }
            try { _allProduction.AddRange(await reporting.GetProductionAsync(filter)); }
            catch (Exception ex) { _analyticsErrors.Add("Výroba: " + ex.Message); }
            try { _allOperators.AddRange(await reporting.GetLoggedOperatorsAsync(filter, Array.Empty<string>(), "", "")); }
            catch (Exception ex) { _analyticsErrors.Add("Operátoři: " + ex.Message); }
            try { _processMessages.AddRange(await enrichment.GetProcessValuesAsync(start,end,Array.Empty<string>(),"",_orderNumber,"","")); }
            catch (Exception ex) { _analyticsErrors.Add("Procesní zprávy: " + ex.Message); }
        }
        Deduplicate();
        _dataLoaded = true;
        _operationSelector.SelectionChanged -= OperationChanged;
        _operationSelector.Items.Clear();
        _operationSelector.Items.Add(new OperationChoice { Caption = "Celá zakázka" });
        foreach (var operation in _operations.Items.OfType<MesProductionOrderOperationRecord>()
                     .GroupBy(x => (x.OperationCode, x.WorkcenterCode))
                     .Select(g => g.First()))
        {
            _operationSelector.Items.Add(new OperationChoice
            {
                Code = operation.OperationCode ?? "",
                Workcenter = operation.WorkcenterCode ?? "",
                Caption = (operation.OperationCode ?? "") + " · " + (operation.WorkcenterCode ?? "")
            });
        }
        // Include operations present only in historical reporting.
        foreach (var op in _allProduction.GroupBy(x => (x.OperationCode, x.WorkcenterCode)).Select(g => g.First()))
        {
            if (_operationSelector.Items.OfType<OperationChoice>().Any(x =>
                x.Code == op.OperationCode && x.Workcenter == op.WorkcenterCode)) continue;
            _operationSelector.Items.Add(new OperationChoice {
                Code = op.OperationCode, Workcenter = op.WorkcenterCode,
                Caption = op.OperationCode + " · " + op.WorkcenterCode
            });
        }
        _operationSelector.SelectedIndex = 0;
        _operationSelector.SelectionChanged += OperationChanged;
        _selectedDay = _allProduction.Select(x => x.Starttime.Date).OrderBy(x => x).Cast<DateTime?>().FirstOrDefault();
        RenderAnalytics();
    }
    private void OperationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_dataLoaded) return;
        _selectedDay = null;
        RenderAnalytics();
    }
    private void Deduplicate()
    {
        var p = _allProduction.GroupBy(x => (x.WorkcenterCode,x.OperationCode,x.Starttime,x.Endtime)).Select(g=>g.First()).ToList();
        var s = _allStates.GroupBy(x => (x.WorkcenterCode,x.Starttime,x.Endtime,x.StateName,x.CustomText)).Select(g=>g.First()).ToList();
        var c = _allCounters.GroupBy(x => (x.WorkcenterCode,x.Timestamp,x.CounterName,x.Value,x.CustomText)).Select(g=>g.First()).ToList();
        var o = _allOperators.GroupBy(x => (x.WorkcenterCode,x.OperationCode,x.HumanCode,x.LoginFrom,x.LoginTo,x.OrderCode)).Select(g=>g.First()).ToList();
        _allProduction.Clear(); _allProduction.AddRange(p);
        _allStates.Clear(); _allStates.AddRange(s);
        _allCounters.Clear(); _allCounters.AddRange(c);
        _allOperators.Clear(); _allOperators.AddRange(o);
        var messages=_processMessages.GroupBy(x => (x.Id,x.Starttime,x.Endtime,x.OperationCode)).Select(g=>g.First()).ToList();
        _processMessages.Clear(); _processMessages.AddRange(messages);
    }
    private void RenderAnalytics()
    {
        _analyticsPanel.Children.Clear();
        _analyticsPanel.Children.Add(Label("Historie výroby a směnová analytika", 21));
        var selection = _operationSelector.SelectedItem as OperationChoice;
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Label("Operace:", 15));
        // ComboBox must be detached from its old parent before reparenting.
        if (_operationSelector.Parent is Panel parent) parent.Children.Remove(_operationSelector);
        header.Children.Add(_operationSelector);
        _analyticsPanel.Children.Add(Card(header));

        var intervals = _allProduction.Where(x => selection == null || string.IsNullOrEmpty(selection.Code)
            || (x.OperationCode == selection.Code && x.WorkcenterCode == selection.Workcenter)).ToList();
        bool FitsOperation(string workcenter, DateTime instant) =>
            selection == null || string.IsNullOrEmpty(selection.Code) ||
            (workcenter == selection.Workcenter &&
             intervals.Any(p => p.Starttime <= instant && (p.Endtime ?? _analyticsTo) > instant));
        var states = _allStates.Where(IsRelevant).Where(s => selection == null || string.IsNullOrEmpty(selection.Code)
            || (s.WorkcenterCode == selection.Workcenter && intervals.Any(p =>
                Overlap(s.Starttime, s.Endtime ?? _analyticsTo, p.Starttime, p.Endtime ?? _analyticsTo)))).ToList();
        var counters = _allCounters.Where(c => FitsOperation(c.WorkcenterCode, c.Timestamp)).ToList();
        var messages = _processMessages.Where(x =>
            string.Equals(x.OrderCode,_orderNumber,StringComparison.OrdinalIgnoreCase) &&
            (selection == null || string.IsNullOrEmpty(selection.Code) ||
             (x.OperationCode == selection.Code && x.WorkcenterCode == selection.Workcenter))).ToList();
        var operators = _allOperators.Where(o => o.HasOperator &&
            string.Equals(o.OrderCode, _orderNumber, StringComparison.OrdinalIgnoreCase) &&
            (selection == null || string.IsNullOrEmpty(selection.Code) ||
             (o.OperationCode == selection.Code && o.WorkcenterCode == selection.Workcenter))).ToList();

        _analyticsPanel.Children.Add(Label($"Intervaly: {intervals.Count} · Stavy: {states.Count} · Čítače: {counters.Count} · Přihlášení: {operators.Count}"));
        if (_analyticsErrors.Count > 0)
            _analyticsPanel.Children.Add(Label("Neúplná data: " + string.Join(" | ", _analyticsErrors.Distinct())));
        if (selection != null && !string.IsNullOrEmpty(selection.Code))
            _analyticsPanel.Children.Add(Label("Poznámka: stavy a čítače nemají ve zdrojových záznamech číslo operace. Přiřazení k operaci vychází z překryvu výrobních intervalů a pracoviště."));

        var stateMinutes = states.Sum(s => Minutes(s.Starttime,s.Endtime ?? _analyticsTo,_analyticsFrom,_analyticsTo));
        var panel = new WrapPanel();
        panel.Children.Add(Card(Label("Zaznamenané stavy\n" + Math.Round(stateMinutes / 60,1).ToString("N1") + " h",18), new Thickness(0,4,8,4)));
        panel.Children.Add(Card(Label("Výrobní intervaly\n" + intervals.Count.ToString("N0"),18), new Thickness(0,4,8,4)));
        panel.Children.Add(Card(Label("Čítačové události\n" + counters.Count.ToString("N0"),18), new Thickness(0,4,8,4)));
        panel.Children.Add(Card(Label("Operátoři\n" + operators.Select(o=>o.HumanCode).Distinct().Count().ToString("N0"),18), new Thickness(0,4,8,4)));
        _analyticsPanel.Children.Add(panel);

        _analyticsPanel.Children.Add(Label("Rozložení stavů zakázky", 18));
        _analyticsPanel.Children.Add(PieChart(states,_analyticsFrom,_analyticsTo));
        _analyticsPanel.Children.Add(Label("Souhrn čítačů", 18));
        _analyticsPanel.Children.Add(Table(counters.GroupBy(x=>new { x.WorkcenterCode,x.CounterName })
            .Select(g=>new {
                Pracoviště=g.Key.WorkcenterCode, Čítač=g.Key.CounterName,
                Hodnota=Math.Round(g.OrderByDescending(x=>x.Timestamp).First().Value,0)
            }).OrderBy(x=>x.Pracoviště).ThenBy(x=>x.Čítač).Cast<object>()));
        _analyticsPanel.Children.Add(Label("Hodnota = poslední zaznamenaná hodnota, nikoli automatický součet. Typ čítače je potřeba ověřit ve FASTEC."));

        _analyticsPanel.Children.Add(Label("Kalendář výroby", 19));
        if (_calendar.Parent is Panel previousParent) previousParent.Children.Remove(_calendar);
        _calendar.SelectedDatesChanged -= CalendarChanged;
        _calendar.DisplayDateStart=_analyticsFrom.Date;
        _calendar.DisplayDateEnd=_analyticsTo.Date;
        _calendar.DisplayDate=_selectedDay ?? _analyticsFrom.Date;
        _calendar.SelectedDate=null;
        _calendar.BlackoutDates.Clear();
        for (var day=_analyticsFrom.Date;day<=_analyticsTo.Date;day=day.AddDays(1))
        {
            var isProduction=intervals.Any(p=>Overlap(p.Starttime,p.Endtime ?? _analyticsTo,day,day.AddDays(1)));
            if (!isProduction) _calendar.BlackoutDates.Add(new CalendarDateRange(day));
        }
        _calendar.SelectedDate=_selectedDay;
        _calendar.SelectedDatesChanged += CalendarChanged;
        _analyticsPanel.Children.Add(Card(_calendar));
        if (_selectedDay.HasValue)
        {
            var clear=new Button { Content="Zobrazit všechny směny",Margin=new Thickness(0,0,0,8),Padding=new Thickness(8) };
            clear.Click+=(_,__)=>{_selectedDay=null;RenderAnalytics();};
            _analyticsPanel.Children.Add(clear);
        }
        var aFrom = _selectedDay ?? _analyticsFrom;
        var aTo = _selectedDay.HasValue ? _selectedDay.Value.AddDays(1) : _analyticsTo;
        if (aFrom < _analyticsFrom) aFrom = _analyticsFrom;
        if (aTo > _analyticsTo) aTo = _analyticsTo;
        if (_selectedDay.HasValue) _analyticsPanel.Children.Add(Label("Vybraný den: " + _selectedDay.Value.ToString("dd.MM.yyyy") + " (kliknutím zrušíš filtr)"));
        var shiftTabs = new TabControl { Margin=new Thickness(0,7,0,12) };
        for (var shift = ShiftBeginning(aFrom); shift < aTo; shift = shift.AddHours(8))
        {
            var start = shift > aFrom ? shift : aFrom;
            var end = shift.AddHours(8) < aTo ? shift.AddHours(8) : aTo;
            if (end <= start) continue;
            var shiftStates = states.Where(s=>Overlap(s.Starttime,s.Endtime ?? _analyticsTo,start,end)).ToList();
            var shiftCounters = counters.Where(c=>c.Timestamp>=start && c.Timestamp<end).ToList();
            var shiftOperators = operators.Where(o=>o.LoginFrom.HasValue &&
                Overlap(o.LoginFrom.Value,o.LoginTo ?? _analyticsTo,start,end)).ToList();
            var tab = new TabItem {
                Header = $"{ShiftLabel(shift)} · {shift:dd.MM.yyyy}",
                ToolTip = $"{start:HH:mm}–{end:HH:mm}"
            };
            var content = new StackPanel { Margin = new Thickness(10) };
            content.Children.Add(Label("Časová osa stavů",16));
            var timeline = new Grid { Height = 30, Margin = new Thickness(0,6,0,8), Background = new SolidColorBrush(Color.FromRgb(85,92,103)) };
            foreach (var state in shiftStates.OrderBy(x=>x.Starttime))
            {
                var from = state.Starttime > start ? state.Starttime : start;
                var to = (state.Endtime ?? _analyticsTo) < end ? (state.Endtime ?? _analyticsTo) : end;
                if (to <= from) continue;
                var left = (from-start).TotalSeconds/(end-start).TotalSeconds;
                var width = (to-from).TotalSeconds/(end-start).TotalSeconds;
                var segment = new Border {
                    Background = StateBrush(state),
                    ToolTip = $"{state.StateName}\n{from:HH:mm:ss}–{to:HH:mm:ss}\n{(to-from).TotalMinutes:N1} min",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(1, width * 1000), Height = 30
                };
                // Canvas gives precise positioning and keeps gaps visible.
                var canvas = timeline.Children.OfType<Canvas>().FirstOrDefault();
                if (canvas == null)
                {
                    canvas = new Canvas { ClipToBounds = true };
                    timeline.Children.Add(canvas);
                    canvas.SizeChanged += (_,__) => {
                        foreach (var b in canvas.Children.OfType<Border>())
                        {
                            if (b.Tag is Tuple<double,double> t) {
                                Canvas.SetLeft(b,t.Item1*canvas.ActualWidth);
                                b.Width=Math.Max(1,t.Item2*canvas.ActualWidth);
                            }
                        }
                    };
                }
                segment.Tag = Tuple.Create(left,width);
                Canvas.SetLeft(segment,left*1000);
                canvas.Children.Add(segment);
            }
            content.Children.Add(timeline);
            content.Children.Add(Label("Stavy a prostoje — podíl a trvání",15));
            content.Children.Add(PieChart(shiftStates,start,end));
            content.Children.Add(Table(shiftStates.GroupBy(x=>x.StateName).Select(g=>new {
                Stav = g.Key, Výskytů = g.Count(),
                Minut = Math.Round(g.Sum(x=>Minutes(x.Starttime,x.Endtime ?? _analyticsTo,start,end)),1),
                PodílProcent = Math.Round(100*g.Sum(x=>Minutes(x.Starttime,x.Endtime ?? _analyticsTo,start,end))/Math.Max(1,(end-start).TotalMinutes),1)
            }).OrderByDescending(x=>x.Minut).Cast<object>()));
            content.Children.Add(Label("Operátoři",15));
            content.Children.Add(Table(shiftOperators.GroupBy(x=>new {x.HumanCode,x.OperatorName})
                .Select(g=>new {
                    OsobníČíslo=g.Key.HumanCode, Jméno=g.Key.OperatorName,
                    Od=g.Min(x=>x.LoginFrom), Do=g.Max(x=>x.LoginTo),
                    Hodin=Math.Round(UnionMinutes(g.Where(x=>x.LoginFrom.HasValue)
                        .Select(x=>(x.LoginFrom.Value,x.LoginTo ?? _analyticsTo)),start,end)/60,2)
                }).Cast<object>()));
            content.Children.Add(Label("Čítače",15));
            content.Children.Add(Table(shiftCounters.GroupBy(c=>c.CounterName)
                .Select(g=>new {
                    Čítač=g.Key, Hodnota=Math.Round(g.OrderByDescending(c=>c.Timestamp).First().Value,0)
                }).OrderBy(x=>x.Čítač).Cast<object>()));
            content.Children.Add(Label("Procesní zprávy",15));
            var shiftMessages=messages.Where(m=>Overlap(m.Starttime,m.Endtime,start,end)).ToList();
            if (shiftMessages.Count == 0) content.Children.Add(Label("Pro tuto směnu nejsou procesní zprávy."));
            else content.Children.Add(Table(shiftMessages.Select(m=>new {
                Od=m.Starttime, Do=m.Endtime, Zpráva=m.StateName
            }).Cast<object>()));
            content.Children.Add(Label("Číselné procesní hodnoty (např. teplota): prozatím chybí ověřený historický SQL zdroj."));
            tab.Content = new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=650 };
            shiftTabs.Items.Add(tab);
        }
        if (shiftTabs.Items.Count == 0) _analyticsPanel.Children.Add(Label("Pro výběr nejsou dostupné směny."));
        else _analyticsPanel.Children.Add(shiftTabs);
    }
    private void CalendarChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_dataLoaded) return;
        var chosen=_calendar.SelectedDate;
        if (!chosen.HasValue || chosen.Value==_selectedDay) return;
        _selectedDay=chosen.Value.Date;
        RenderAnalytics();
    }
}
