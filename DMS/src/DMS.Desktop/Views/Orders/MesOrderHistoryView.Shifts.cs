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
    private sealed class MesordV6ShiftWindow
    {
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public string Name { get; init; } = "";
        public string SourceName { get; init; } = "";
        public DateTime ShiftDay { get; init; }
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


    private List<MesordV6ShiftWindow> GetShiftWindowsV6() =>
        GetShiftWindowsFromDatabaseV61();


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
        UnionMinutesAcrossWindowsV62(new[] { (from, to) }, windows);


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
                SoučetHodnot = Math.Round(g.Sum(c => c.Value), 2)
            })
            .OrderBy(x => x.Pracoviště).ThenBy(x => x.Čítač).Cast<object>();
}
