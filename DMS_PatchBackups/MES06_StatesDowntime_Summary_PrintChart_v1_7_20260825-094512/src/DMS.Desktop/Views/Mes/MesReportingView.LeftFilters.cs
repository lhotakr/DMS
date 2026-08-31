using DMS.Integration.Mes.Reporting.Definitions;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class Mes06FilterChoice
    {
        public Mes06FilterChoice(
            string code,
            string text)
        {
            Code = code;
            Text = text;
        }

        public string Code { get; }
        public string Text { get; }

        public override string ToString() => Text;
    }

    private bool _mes06InitializingFilters;

    private void InitializeLeftFilterPanel()
    {
        _mes06InitializingFilters = true;

        try
        {
            TxtFilterTitle.Text =
                T(
                    "MES06.FilterPanel.Title",
                    "Filter");

            ExpReport.Header =
                T(
                    "MES06.FilterPanel.Report",
                    "Report");

            ExpPeriod.Header =
                T(
                    "MES06.FilterPanel.Period",
                    "Period");

            ExpWorkcenters.Header =
                T(
                    "MES06.FilterPanel.Workcenters",
                    "Work centers");

            ExpShift.Header =
                T(
                    "MES06.FilterPanel.Shift",
                    "Shift");

            ExpPda.Header =
                T(
                    "MES06.FilterPanel.Pda",
                    "PDA");

            LblQuickPeriod.Text =
                T(
                    "MES06.Filter.QuickSelection",
                    "Quick");

            LblShift.Text =
                T(
                    "MES06.Filter.Shift",
                    "Shift");

            LblOperation.Text =
                T(
                    "MES06.Filter.Operation",
                    "Operation");

            BtnLoad.Content =
                T(
                    "MES06.Button.Apply",
                    "Apply");

            BtnResetFilters.Content =
                T(
                    "MES06.Button.Reset",
                    "Reset");

            CmbQuickPeriod.ItemsSource =
                new[]
                {
                    new Mes06FilterChoice(
                        "TODAY",
                        T(
                            "MES06.Quick.Today",
                            "Today")),
                    new Mes06FilterChoice(
                        "YESTERDAY",
                        T(
                            "MES06.Quick.Yesterday",
                            "Yesterday")),
                    new Mes06FilterChoice(
                        "LAST7",
                        T(
                            "MES06.Quick.Last7Days",
                            "Last 7 days")),
                    new Mes06FilterChoice(
                        "THISWEEK",
                        T(
                            "MES06.Quick.ThisWeek",
                            "This week")),
                    new Mes06FilterChoice(
                        "CUSTOM",
                        T(
                            "MES06.Quick.Custom",
                            "Custom"))
                };

            CmbQuickPeriod.SelectedIndex = 0;

            CmbShift.ItemsSource =
                new[]
                {
                    new Mes06FilterChoice(
                        string.Empty,
                        T(
                            "MES06.Filter.AllShifts",
                            "All shifts"))
                };

            CmbShift.SelectedIndex = 0;
        }
        finally
        {
            _mes06InitializingFilters = false;
        }
    }

    private void CmbQuickPeriod_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_mes06InitializingFilters
            || CmbQuickPeriod.SelectedItem
                is not Mes06FilterChoice choice)
        {
            return;
        }

        var today =
            DateTime.Today;

        switch (choice.Code)
        {
            case "TODAY":
                DateFrom.SelectedDate =
                    today;

                DateTo.SelectedDate =
                    today.AddDays(1);
                break;

            case "YESTERDAY":
                DateFrom.SelectedDate =
                    today.AddDays(-1);

                DateTo.SelectedDate =
                    today;
                break;

            case "LAST7":
                DateFrom.SelectedDate =
                    today.AddDays(-6);

                DateTo.SelectedDate =
                    today.AddDays(1);
                break;

            case "THISWEEK":
                var offset =
                    ((int)today.DayOfWeek + 6) % 7;

                var monday =
                    today.AddDays(-offset);

                DateFrom.SelectedDate =
                    monday;

                DateTo.SelectedDate =
                    monday.AddDays(7);
                break;

            case "CUSTOM":
                break;
        }
    }

    private void BtnResetFilters_Click(
        object sender,
        RoutedEventArgs e)
    {
        _mes06InitializingFilters = true;

        try
        {
            TxtProduct.Clear();
            TxtOrder.Clear();
            TxtOperation.Clear();

            SelectAllWorkcenters(
                true);

            if (CmbShift.Items.Count > 0)
            {
                CmbShift.SelectedIndex = 0;
            }

            CmbQuickPeriod.SelectedIndex = 0;

            DateFrom.SelectedDate =
                DateTime.Today;

            DateTo.SelectedDate =
                DateTime.Today.AddDays(1);
        }
        finally
        {
            _mes06InitializingFilters = false;
        }
    }

    private void RefreshShiftChoices(
        IReadOnlyList<object> rows)
    {
        var previous =
            (CmbShift.SelectedItem
                as Mes06FilterChoice)
            ?.Code
            ?? string.Empty;

        var names =
            _mes06ShiftEvents
                .Select(shift =>
                    shift.Name?.Trim())
                .Where(name =>
                    !string.IsNullOrWhiteSpace(
                        name))
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name =>
                    _mes06ShiftEvents
                        .Where(shift =>
                            string.Equals(
                                shift.Name,
                                name,
                                StringComparison.CurrentCultureIgnoreCase))
                        .Min(shift =>
                            shift.Starttime.TimeOfDay))
                .ThenBy(name =>
                    name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        // Fallback for older/report-specific DTOs if the enrichment query fails.
        if (names.Count == 0)
        {
            names =
                rows
                    .Select(row =>
                        FirstNonEmpty(
                            ReadProperty(
                                row,
                                "ShiftName"),
                            ReadProperty(
                                row,
                                "Shift"),
                            ReadProperty(
                                row,
                                "ShiftCode")))
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                    .Distinct(
                        StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(value =>
                        value,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
        }

        var choices =
            new List<Mes06FilterChoice>
            {
                new(
                    string.Empty,
                    T(
                        "MES06.Filter.AllShifts",
                        "All shifts"))
            };

        choices.AddRange(
            names.Select(name =>
                new Mes06FilterChoice(
                    name!,
                    name!)));

        CmbShift.ItemsSource =
            choices;

        CmbShift.SelectedItem =
            choices.FirstOrDefault(choice =>
                string.Equals(
                    choice.Code,
                    previous,
                    StringComparison.CurrentCultureIgnoreCase))
            ?? choices[0];
    }

    private IReadOnlyList<object> ApplyProductionLeftFilters(
        MesReportDefinition definition,
        IReadOnlyList<object> rows)
    {
        if (!IsProductionReport(
                definition))
        {
            return rows;
        }

        var shift =
            (CmbShift.SelectedItem
                as Mes06FilterChoice)
            ?.Code
            ?.Trim()
            ?? string.Empty;

        var operation =
            TxtOperation.Text?.Trim()
            ?? string.Empty;

        var query =
            rows.AsEnumerable();

        if (!_mes06CounterReportMode)
        {
            query =
                query.Where(
                    HasProductionData);
        }

        return query
            .Where(row =>
                MatchesShift(
                    row,
                    shift)
                && MatchesOperation(
                    row,
                    operation))
            .ToList();
    }

    private static bool HasProductionData(
        object row)
    {
        foreach (var propertyName in new[]
                 {
                     "Total",
                     "Good",
                     "Bad",
                     "Rework",
                     "UtilizationSeconds",
                     "DowntimeSeconds",
                     "PerformanceTotal",
                     "PerformanceGood",
                     "PerformanceBad",
                     "DurationUtilization",
                     "DurationDown"
                 })
        {
            var raw =
                ReadProperty(
                    row,
                    propertyName);

            if (raw is null)
            {
                continue;
            }

            try
            {
                if (Convert.ToDecimal(
                        raw,
                        CultureInfo.InvariantCulture) != 0m)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore non-numeric properties and continue probing.
            }
        }

        return false;
    }

    private bool MatchesShift(
        object row,
        string shift)
    {
        if (string.IsNullOrWhiteSpace(
                shift))
        {
            return true;
        }

        var databaseShift =
            ResolveDatabaseShift(
                row);

        if (databaseShift is not null)
        {
            return string.Equals(
                databaseShift.Name,
                shift,
                StringComparison.CurrentCultureIgnoreCase);
        }

        var value =
            FirstNonEmpty(
                ReadProperty(
                    row,
                    "ShiftName"),
                ReadProperty(
                    row,
                    "Shift"),
                ReadProperty(
                    row,
                    "ShiftCode"));

        return string.Equals(
            value,
            shift,
            StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool MatchesOperation(
        object row,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(
                operation))
        {
            return true;
        }

        var value =
            FirstNonEmpty(
                ReadProperty(
                    row,
                    "OperationCode"),
                ReadProperty(
                    row,
                    "Operation"),
                ReadProperty(
                    row,
                    "RoutingCode"));

        return !string.IsNullOrWhiteSpace(
                   value)
               && value.Contains(
                   operation,
                   StringComparison.CurrentCultureIgnoreCase);
    }
}
