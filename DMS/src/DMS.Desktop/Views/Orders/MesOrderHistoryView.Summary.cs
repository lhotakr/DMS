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
    private void AddExportControlsV6()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var excel = new Button { Content = MesordText("Exportovat XLSX"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var pdf = new Button { Content = MesordText("Exportovat PDF"), Padding = new Thickness(12, 6, 12, 6) };
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
            _analyticsPanel.Children.Add(Label(MesordText("Směnový den") + ": " + _selectedDay.Value.ToString("dd.MM.yyyy") + " (FASTEC DimShiftEvent)"));
        else
            _analyticsPanel.Children.Add(Label("Všechny směnové dny zakázky – sloučeno podle typu směny"));

        var tabs = new TabControl { Margin = new Thickness(0, 7, 0, 12) };
        if (data.Windows.Count == 0)
            _analyticsPanel.Children.Add(Label("FASTEC: žádné směnové intervaly pro vybrané období. Směny se neodhadují."));
        var shiftTabs = MesordV6ShiftNames.Concat(data.Windows.Select(w => w.Name)
            .Where(name => !MesordV6ShiftNames.Contains(name))).Distinct();
        foreach (var name in shiftTabs)
        {
            var windows = data.Windows.Where(w => w.Name == name).ToList();
            var states = data.States.Where(s => OverlapsWindowsV6(s.Starttime, s.Endtime ?? _analyticsTo, windows)).ToList();
            var counters = data.Counters.Where(c => InWindowsV6(c.Timestamp, windows)).ToList();
            var operators = data.Operators.Where(o => o.LoginFrom.HasValue &&
                OverlapsWindowsV6(o.LoginFrom.Value, o.LoginTo ?? _analyticsTo, windows)).ToList();
            var messages = data.Messages.Where(m => OverlapsWindowsV6(m.Starttime, m.Endtime, windows)).ToList();

            var content = new StackPanel { Margin = new Thickness(8) };
            // MESORD v6.1: redundant shift banner removed.

            // Two different views: state share by duration and actual chronological sequence.
            content.Children.Add(Label("Stavy a prostoje – podíl a trvání", 16));
            content.Children.Add(CreateShiftPieV61(states, windows));
            content.Children.Add(Label("Průběh směny – chronologie stavů", 16));
            var stateTimelines = new StackPanel();
            foreach (var window in windows)
            {
                var w = window;
                var windowStates = states.Where(s => Overlap(s.Starttime, s.Endtime ?? _analyticsTo, w.Start, w.End)).ToList();
                var expander = new Expander
                {
                    Header = $"{w.ShiftDay:dd.MM.yyyy} · {w.Start:HH:mm}–{w.End:HH:mm} · FASTEC: {w.SourceName}",
                    Margin = new Thickness(0, 3, 0, 3)
                };
                if (windows.Count <= 2)
                {
                    expander.Content = CreateShiftStateTimelineV61(windowStates, w.Start, w.End);
                    expander.IsExpanded = true;
                }
                else
                {
                    expander.Expanded += (_, _) =>
                    {
                        if (expander.Content == null)
                            expander.Content = CreateShiftStateTimelineV61(windowStates, w.Start, w.End);
                    };
                }
                stateTimelines.Children.Add(expander);
            }
            content.Children.Add(stateTimelines);

            // MES06 process-message timeline stays immediately above the three tables.
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
            var totalStateMinutes = states.GroupBy(s => s.StateName)
                .Sum(g => StateMinutesV62(g, windows));
            var stateRows = states.GroupBy(s => s.StateName)
                .Select(g => new
                {
                    Stav = g.Key,
                    Minut = Math.Round(StateMinutesV62(g, windows), 1),
                    PodílProcent = Math.Round(100 * StateMinutesV62(g, windows) / Math.Max(1, totalStateMinutes), 1)
                }).OrderByDescending(x => x.Minut).Cast<object>();
            var operatorRows = OperatorShiftRowsV63(operators, windows);
            var counterRows = CounterOrderSummaryV62(counters);
            AddSummaryColumnV6(summary, 0, "Stavy", stateRows);
            AddSummaryColumnV6(summary, 1, "Operátoři – přihlášení", operatorRows);
            AddSummaryColumnV6(summary, 2, "Čítače – součet hodnot", counterRows);
            content.Children.Add(new ScrollViewer { Content = summary, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });

            var tab = new TabItem { Header = MesordText(name), Content = new ScrollViewer {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 850 } };
            tabs.Items.Add(tab);
        }
        _analyticsPanel.Children.Add(tabs);
    }


    private void AddSummaryColumnV6(Grid grid, int column, string title, IEnumerable<object> values)
    {
        var holder = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0) };
        holder.Children.Add(Label(title, 16));
        holder.Children.Add(Table(values));
        Grid.SetColumn(holder, column);
        grid.Children.Add(holder);
    }
}
