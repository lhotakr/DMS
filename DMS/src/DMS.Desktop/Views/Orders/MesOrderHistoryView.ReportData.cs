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
    private MesProductionOrderRecord? _mesordExportOrderV63;

    private CultureInfo MesordCsV63
    {
        get
        {
            try { return CultureInfo.GetCultureInfo(_mesordCulture); }
            catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("cs-CZ"); }
        }
    }

    private string AuthorV63 => string.IsNullOrWhiteSpace(_mesordUserDisplayName) ? Environment.UserName : _mesordUserDisplayName;

    private const string Plant9200V63 = "Zatím nedoplněno (doplní kvalita)";

    private sealed class OperatorRowV63
    {
        public string Workcenter { get; init; } = "";
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTime Login { get; init; }
        public DateTime? Logout { get; init; }
        public double HoursInWindow { get; init; }
        public DateTime ClippedFrom { get; init; }
        public DateTime ClippedTo { get; init; }
    }

    // Actual login/logout times are preserved. Only the duration is clipped to
    // the selected FASTEC shift, so cross-shift sessions are not misrepresented.

    // Merge only overlapping/adjacent time ranges of the SAME person on the SAME
    // workplace. Preserve genuinely distinct sessions and independent workplaces.
    private List<OperatorRowV63> OperatorSessionsV63(IEnumerable<MesLoggedOperatorRecord> source,
        IReadOnlyList<MesordV6ShiftWindow> windows)
    {
        var rows = new List<OperatorRowV63>();
        foreach (var o in source.Where(o => o.LoginFrom.HasValue))
        {
            var login = o.LoginFrom!.Value;
            var logout = o.LoginTo ?? _analyticsTo;
            foreach (var w in windows)
            {
                var a = login > w.Start ? login : w.Start;
                var b = logout < w.End ? logout : w.End;
                if (b <= a) continue;
                rows.Add(new OperatorRowV63
                {
                    Workcenter = o.WorkcenterCode ?? "",
                    Code = o.HumanCode ?? "",
                    Name = o.OperatorName ?? "",
                    Login = login, Logout = o.LoginTo,
                    ClippedFrom = a, ClippedTo = b,
                    HoursInWindow = (b - a).TotalHours
                });
            }
        }
        var merged = new List<OperatorRowV63>();
        foreach (var group in rows.GroupBy(r =>
            ((r.Workcenter ?? "").ToUpperInvariant(), (r.Code ?? "").ToUpperInvariant(),
             (r.Name ?? "").ToUpperInvariant())))
        {
            OperatorRowV63? current = null;
            foreach (var next in group.OrderBy(r => r.ClippedFrom).ThenBy(r => r.ClippedTo))
            {
                if (current == null) { current = next; continue; }
                if (next.ClippedFrom <= current.ClippedTo)
                {
                    var b = next.ClippedTo > current.ClippedTo ? next.ClippedTo : current.ClippedTo;
                    current = new OperatorRowV63
                    {
                        Workcenter = current.Workcenter,
                        Code = current.Code, Name = current.Name,
                        Login = current.Login <= next.Login ? current.Login : next.Login,
                        Logout = !current.Logout.HasValue || !next.Logout.HasValue ? null :
                            (current.Logout.Value >= next.Logout.Value ? current.Logout : next.Logout),
                        ClippedFrom = current.ClippedFrom,
                        ClippedTo = b,
                        HoursInWindow = (b - current.ClippedFrom).TotalHours
                    };
                }
                else { merged.Add(current); current = next; }
            }
            if (current != null) merged.Add(current);
        }
        return merged.OrderBy(r => r.Login).ThenBy(r => r.Code)
            .ThenBy(r => r.Workcenter).ToList();
    }

    private IEnumerable<object> OperatorShiftRowsV63(IEnumerable<MesLoggedOperatorRecord> operators,
        IReadOnlyList<MesordV6ShiftWindow> windows) =>
        OperatorSessionsV63(operators, windows).Select(o => (object)new
        {
            OsobníČíslo = o.Code,
            Jméno = o.Name,
            Přihlášení = o.Login.ToString("dd.MM.yyyy HH:mm", MesordCsV63),
            Odhlášení = o.Logout?.ToString("dd.MM.yyyy HH:mm", MesordCsV63) ?? MesordText("Stále přihlášen"),
            HodinVeSměně = Math.Round(o.HoursInWindow, 2)
        });


    // The overall order must not be restricted to shift-event coverage, which may be partial.
    private IReadOnlyList<MesordV6ShiftWindow> WholeOrderWindow() => new[]
    {
        new MesordV6ShiftWindow { Start = _analyticsFrom, End = _analyticsTo,
            Name = "Order", ShiftDay = _analyticsFrom.Date }
    };

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


    private static string OrderDatesV63(DateTime from, DateTime to) =>
        $"{from:dd.MM.yyyy HH:mm} - {to:dd.MM.yyyy HH:mm}";
}
