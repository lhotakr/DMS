using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Mes;

/// <summary>
/// FASTEC-like quick period selector for MES06.
/// Keeps the existing MES06 filter panel untouched and replaces only the
/// quick-period list/selection logic after the view has been initialized.
/// </summary>
public partial class MesReportingView
{
    private bool _fastecQuickPeriodsInitialized;

    private void InitializeFastecQuickPeriods()
    {
        if (_fastecQuickPeriodsInitialized)
        {
            return;
        }

        _fastecQuickPeriodsInitialized = true;

        // Detach the original handler from MesReportingView.LeftFilters.cs.
        CmbQuickPeriod.SelectionChanged -=
            CmbQuickPeriod_SelectionChanged;

        var currentCode =
            (CmbQuickPeriod.SelectedItem as Mes06FilterChoice)
            ?.Code
            ?? "YESTERDAY";

        CmbQuickPeriod.ItemsSource =
            BuildFastecQuickPeriodChoices();

        CmbQuickPeriod.SelectedItem =
            CmbQuickPeriod.Items
                .OfType<Mes06FilterChoice>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Code,
                        currentCode,
                        StringComparison.OrdinalIgnoreCase))
            ?? CmbQuickPeriod.Items
                .OfType<Mes06FilterChoice>()
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Code,
                        "YESTERDAY",
                        StringComparison.OrdinalIgnoreCase))
            ?? CmbQuickPeriod.Items
                .OfType<Mes06FilterChoice>()
                .FirstOrDefault();

        CmbQuickPeriod.SelectionChanged +=
            CmbQuickPeriod_FastecSelectionChanged;
    }

    private IReadOnlyList<Mes06FilterChoice> BuildFastecQuickPeriodChoices()
    {
        var today =
            DateTime.Today;

        var thisWeekStart =
            GetMonday(
                today);

        var lastWeekStart =
            thisWeekStart.AddDays(-7);

        var previousWeekStart =
            thisWeekStart.AddDays(-14);

        var thisMonth =
            new DateTime(
                today.Year,
                today.Month,
                1);

        var lastMonth =
            thisMonth.AddMonths(-1);

        var previousMonth =
            thisMonth.AddMonths(-2);

        return new[]
        {
            new Mes06FilterChoice(
                "CUSTOM",
                LocalQuickText(
                    "Definováno uživatelem",
                    "User defined",
                    "Benutzerdefiniert")),

            new Mes06FilterChoice(
                "TODAY",
                LocalQuickText(
                    "Dnes",
                    "Today",
                    "Heute")),

            new Mes06FilterChoice(
                "YESTERDAY",
                LocalQuickText(
                    "Včera",
                    "Yesterday",
                    "Gestern")),

            new Mes06FilterChoice(
                "THISWEEK",
                string.Format(
                    LocalQuickText(
                        "Aktuální týden ({0})",
                        "Current week ({0})",
                        "Aktuelle Woche ({0})"),
                    ISOWeek.GetWeekOfYear(
                        thisWeekStart))),

            new Mes06FilterChoice(
                "LASTWEEK",
                string.Format(
                    LocalQuickText(
                        "Minulý týden ({0})",
                        "Last week ({0})",
                        "Letzte Woche ({0})"),
                    ISOWeek.GetWeekOfYear(
                        lastWeekStart))),

            new Mes06FilterChoice(
                "PREVIOUSWEEK",
                string.Format(
                    LocalQuickText(
                        "Předposlední týden ({0})",
                        "Week before last ({0})",
                        "Vorletzte Woche ({0})"),
                    ISOWeek.GetWeekOfYear(
                        previousWeekStart))),

            new Mes06FilterChoice(
                "THISMONTH",
                string.Format(
                    LocalQuickText(
                        "Aktuální měsíc ({0})",
                        "Current month ({0})",
                        "Aktueller Monat ({0})"),
                    GetLocalizedMonthName(
                        thisMonth))),

            new Mes06FilterChoice(
                "LASTMONTH",
                string.Format(
                    LocalQuickText(
                        "Minulý měsíc ({0})",
                        "Last month ({0})",
                        "Letzter Monat ({0})"),
                    GetLocalizedMonthName(
                        lastMonth))),

            new Mes06FilterChoice(
                "PREVIOUSMONTH",
                string.Format(
                    LocalQuickText(
                        "Předposlední měsíc ({0})",
                        "Month before last ({0})",
                        "Vorletzter Monat ({0})"),
                    GetLocalizedMonthName(
                        previousMonth))),

            new Mes06FilterChoice(
                "THISYEAR",
                string.Format(
                    LocalQuickText(
                        "Aktuální ročník ({0})",
                        "Current year ({0})",
                        "Aktuelles Jahr ({0})"),
                    today.Year)),

            new Mes06FilterChoice(
                "LASTYEAR",
                string.Format(
                    LocalQuickText(
                        "Minulý rok ({0})",
                        "Last year ({0})",
                        "Letztes Jahr ({0})"),
                    today.Year - 1)),

            new Mes06FilterChoice(
                "LAST7",
                LocalQuickText(
                    "Posledních 7 dní",
                    "Last 7 days",
                    "Letzte 7 Tage")),

            new Mes06FilterChoice(
                "LAST30",
                LocalQuickText(
                    "Posledních 30 dní",
                    "Last 30 days",
                    "Letzte 30 Tage"))
        };
    }

    private void CmbQuickPeriod_FastecSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_mes06InitializingFilters
            || CmbQuickPeriod.SelectedItem
                is not Mes06FilterChoice choice)
        {
            return;
        }

        ApplyFastecQuickPeriod(
            choice.Code);
    }

    private void ApplyFastecQuickPeriod(
        string code)
    {
        var today =
            DateTime.Today;

        switch (
            code?.Trim().ToUpperInvariant())
        {
            case "CUSTOM":
                return;

            case "TODAY":
                SetQuickPeriod(
                    today,
                    today.AddDays(1));
                return;

            case "YESTERDAY":
                SetQuickPeriod(
                    today.AddDays(-1),
                    today);
                return;

            case "THISWEEK":
            {
                var monday =
                    GetMonday(
                        today);

                SetQuickPeriod(
                    monday,
                    monday.AddDays(7));
                return;
            }

            case "LASTWEEK":
            {
                var monday =
                    GetMonday(
                        today)
                        .AddDays(-7);

                SetQuickPeriod(
                    monday,
                    monday.AddDays(7));
                return;
            }

            case "PREVIOUSWEEK":
            {
                var monday =
                    GetMonday(
                        today)
                        .AddDays(-14);

                SetQuickPeriod(
                    monday,
                    monday.AddDays(7));
                return;
            }

            case "THISMONTH":
            {
                var first =
                    new DateTime(
                        today.Year,
                        today.Month,
                        1);

                SetQuickPeriod(
                    first,
                    first.AddMonths(1));
                return;
            }

            case "LASTMONTH":
            {
                var first =
                    new DateTime(
                        today.Year,
                        today.Month,
                        1)
                        .AddMonths(-1);

                SetQuickPeriod(
                    first,
                    first.AddMonths(1));
                return;
            }

            case "PREVIOUSMONTH":
            {
                var first =
                    new DateTime(
                        today.Year,
                        today.Month,
                        1)
                        .AddMonths(-2);

                SetQuickPeriod(
                    first,
                    first.AddMonths(1));
                return;
            }

            case "THISYEAR":
            {
                var first =
                    new DateTime(
                        today.Year,
                        1,
                        1);

                SetQuickPeriod(
                    first,
                    first.AddYears(1));
                return;
            }

            case "LASTYEAR":
            {
                var first =
                    new DateTime(
                        today.Year - 1,
                        1,
                        1);

                SetQuickPeriod(
                    first,
                    first.AddYears(1));
                return;
            }

            case "LAST7":
                SetQuickPeriod(
                    today.AddDays(-6),
                    today.AddDays(1));
                return;

            case "LAST30":
                SetQuickPeriod(
                    today.AddDays(-29),
                    today.AddDays(1));
                return;
        }
    }

    private void SetQuickPeriod(
        DateTime from,
        DateTime to)
    {
        _mes06InitializingFilters =
            true;

        try
        {
            DateFrom.SelectedDate =
                from.Date;

            // MES06 consistently treats DateTo as an exclusive upper boundary.
            DateTo.SelectedDate =
                to.Date;
        }
        finally
        {
            _mes06InitializingFilters =
                false;
        }
    }

    private static DateTime GetMonday(
        DateTime date)
    {
        var offset =
            ((int)date.DayOfWeek + 6)
            % 7;

        return date.Date.AddDays(
            -offset);
    }

    private static string GetLocalizedMonthName(
        DateTime date)
    {
        var culture =
            CultureInfo.CurrentUICulture;

        var value =
            culture.DateTimeFormat
                .GetMonthName(
                    date.Month);

        if (string.IsNullOrWhiteSpace(
                value))
        {
            value =
                date.ToString(
                    "MMMM",
                    culture);
        }

        return value;
    }

    private static string LocalQuickText(
        string cs,
        string en,
        string de)
    {
        var language =
            CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName;

        return language switch
        {
            "cs" => cs,
            "de" => de,
            _ => en
        };
    }
}
