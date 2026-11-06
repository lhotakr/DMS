using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.Orders;

public sealed partial class MesOrderHistoryView
{
    private readonly StackPanel _analyticsPanel = new() { Margin = new Thickness(0, 18, 0, 0) };

    private static DateTime ShiftBeginning(DateTime instant)
    {
        var day = instant.Date;
        var time = instant.TimeOfDay;
        if (time < new TimeSpan(5, 55, 0)) return day.AddDays(-1).AddHours(21).AddMinutes(55);
        if (time < new TimeSpan(13, 55, 0)) return day.AddHours(5).AddMinutes(55);
        if (time < new TimeSpan(21, 55, 0)) return day.AddHours(13).AddMinutes(55);
        return day.AddHours(21).AddMinutes(55);
    }

    private static DateTime ShiftEnding(DateTime start) => start.AddHours(8);

    private static string ShiftLabel(DateTime start) => start.Hour switch
    {
        5 => "Ranní", 13 => "Odpolední", _ => "Noční"
    };

    private static TextBlock Label(string text, int size = 14) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 5)
    };

    private static DataGrid Table(IEnumerable<object> rows)
    {
        return new DataGrid
        {
            ItemsSource = rows.ToList(), AutoGenerateColumns = true,
            IsReadOnly = true, CanUserAddRows = false, MinHeight = 100,
            MaxHeight = 330, Margin = new Thickness(0, 3, 0, 12)
        };
    }

    private async Task LoadAnalyticsAsync(MesDatabaseConnectionSettings settings,
        DateTime? actualStart, DateTime? actualEnd)
    {
        _analyticsPanel.Children.Clear();
        _analyticsPanel.Children.Add(Label("Analytika zakázky – směny, stavy a čítače", 20));
        if (!actualStart.HasValue)
        {
            _analyticsPanel.Children.Add(Label("Zakázka nemá skutečný začátek. Historické období nelze určit."));
            return;
        }
        var from = actualStart.Value;
        var to = actualEnd ?? DateTime.Now;
        if (to <= from) { _analyticsPanel.Children.Add(Label("Neplatné časové období zakázky.")); return; }
        var reporting = new MesReportingDataService(settings);
        var states = new List<MesStateRecord>();
        var counters = new List<MesCounterRecord>();
        var production = new List<MesProductionRecord>();
        var errors = new List<string>();
        // Query in bounded windows so long-running orders do not hit the report row cap.
        for (var start = from; start < to; start = start.AddDays(1))
        {
            var end = start.AddDays(1) < to ? start.AddDays(1) : to;
            var filter = new MesReportFilter { From = start, To = end,
                OrderCode = _orderNumber, MaxRows = 50000 };
            try { states.AddRange(await reporting.GetStatesAsync(filter)); }
            catch (Exception ex) { errors.Add("Stavy: " + ex.Message); }
            try { counters.AddRange(await reporting.GetCountersAsync(filter)); }
            catch (Exception ex) { errors.Add("Čítače: " + ex.Message); }
            try { production.AddRange(await reporting.GetProductionAsync(filter)); }
            catch (Exception ex) { errors.Add("Výroba: " + ex.Message); }
        }
        // Daily query windows can repeat overlapping intervals: remove exact duplicates.
        states = states.GroupBy(x => (x.WorkcenterCode, x.Starttime, x.Endtime, x.StateName, x.CustomText)).Select(g => g.First()).ToList();
        counters = counters.GroupBy(x => (x.WorkcenterCode, x.Timestamp, x.CounterName, x.Value, x.CustomText)).Select(g => g.First()).ToList();
        production = production.GroupBy(x => (x.WorkcenterCode, x.OperationCode, x.Starttime, x.Endtime)).Select(g => g.First()).ToList();
        _analyticsPanel.Children.Add(Label($"Historické záznamy: {production.Count} výrobních intervalů, {states.Count} stavů, {counters.Count} čítačových událostí."));
        _analyticsPanel.Children.Add(Label("Poznámka: hodnoty čítačů jsou jednotlivé události. Bez ověření typu čítače se nesčítají (mohou být kumulativní)."));
        if (errors.Count > 0) _analyticsPanel.Children.Add(Label("Neúplná data: " + string.Join(" | ", errors.Distinct())));
        var totals = states.GroupBy(s => string.IsNullOrWhiteSpace(s.CategoryName) ? s.StateName : s.CategoryName)
            .Select(g => new { Stav = g.Key, DobaHodin = Math.Round(g.Sum(s => Math.Max(0, s.DurationSeconds)) / 3600, 2) })
            .OrderByDescending(x => x.DobaHodin).Cast<object>().ToList();
        _analyticsPanel.Children.Add(Label("Souhrn stavů zakázky", 17));
        _analyticsPanel.Children.Add(Table(totals));
        _analyticsPanel.Children.Add(Label("Souhrn čítačových událostí", 17));
        _analyticsPanel.Children.Add(Table(counters.GroupBy(x => x.CounterName)
            .Select(g => new { Čítač = g.Key, Událostí = g.Count(), PosledníHodnota = g.OrderByDescending(x => x.Timestamp).First().Value })
            .OrderBy(x => x.Čítač).Cast<object>()));
        for (var shift = ShiftBeginning(from); shift < to; shift = ShiftEnding(shift))
        {
            var a = shift > from ? shift : from;
            var b = ShiftEnding(shift) < to ? ShiftEnding(shift) : to;
            var shiftStates = states.Where(s => s.Starttime < b && (s.Endtime ?? to) > a).ToList();
            var shiftCounters = counters.Where(c => c.Timestamp >= a && c.Timestamp < b).ToList();
            var shiftProduction = production.Where(p => p.Starttime < b && (p.Endtime ?? to) > a).ToList();
            var group = new Expander { Header = $"{ShiftLabel(shift)} směna {shift:dd.MM.yyyy} · {a:HH:mm}–{b:HH:mm}",
                IsExpanded = false, Margin = new Thickness(0, 5, 0, 5) };
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(Label($"Výrobních intervalů: {shiftProduction.Count} · Stavů: {shiftStates.Count} · Čítačových událostí: {shiftCounters.Count}"));
            panel.Children.Add(Label("Stavy a prostoje (podíl ze sledované části směny)", 15));
            panel.Children.Add(Table(shiftStates.GroupBy(s => s.StateName)
                .Select(g => new {
                    Stav = g.Key, Výskytů = g.Count(),
                    Minut = Math.Round(g.Sum(s => Math.Max(0, (((s.Endtime ?? b) < b ? (s.Endtime ?? b) : b) - (s.Starttime > a ? s.Starttime : a)).TotalMinutes)), 1),
                    PodílProcent = Math.Round(100 * g.Sum(s => Math.Max(0, (((s.Endtime ?? b) < b ? (s.Endtime ?? b) : b) - (s.Starttime > a ? s.Starttime : a)).TotalMinutes)) / Math.Max(1, (b - a).TotalMinutes), 1)
                }).OrderByDescending(x => x.Minut).Cast<object>()));
            panel.Children.Add(Label("Čítače – historické události", 15));
            panel.Children.Add(Table(shiftCounters.Select(c => new { Čas = c.Timestamp, Pracoviště = c.WorkcenterCode, Čítač = c.CounterName, Hodnota = c.Value }).Cast<object>()));
            panel.Children.Add(Label("Operátoři: historické přiřazení k zakázce čeká na ověření vazby v MES."));
            panel.Children.Add(Label("Procesní hodnoty: volitelné, zatím nejsou v tomto přehledu napojené."));
            group.Content = panel;
            _analyticsPanel.Children.Add(group);
        }
    }
}
