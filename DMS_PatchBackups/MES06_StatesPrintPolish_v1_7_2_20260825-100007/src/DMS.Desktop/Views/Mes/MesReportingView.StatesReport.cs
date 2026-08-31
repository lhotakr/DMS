using DMS.Integration.Mes.Reporting.Definitions;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class Mes06StateSummaryRow
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string StateName { get; init; } = string.Empty;
        public int Occurrences { get; init; }
        public double DurationSeconds { get; init; }
        public string DurationText { get; init; } = string.Empty;
        public double SharePercent { get; init; }
    }

    private void BuildStatesReportPresentation(
        MesReportDefinition definition)
    {
        BuildColumns(
            definition);

        RebindDynamicColumnsForDisplayRows(
            definition);

        RemoveEmptyStateNoteColumn();

        GridReport.Columns.Insert(
            0,
            new DataGridTextColumn
            {
                Header =
                    T(
                        "MES06.Column.Shift",
                        "Shift"),
                Binding =
                    new Binding(
                        "[ShiftName]")
                    {
                        Mode =
                            BindingMode.OneWay
                    },
                Width =
                    new DataGridLength(
                        145d)
            });

        GridReport.ItemsSource =
            _currentRows
                .OrderBy(
                    ResolveRowStart)
                .Select(row =>
                    new Mes06GridRow(
                        row,
                        ResolveStateShiftName(
                            row),
                        string.Empty))
                .ToList();

        BuildStateDowntimeSummary();
    }

    private string ResolveStateShiftName(
        object row)
    {
        var databaseShift =
            ResolveDatabaseShift(
                row);

        if (databaseShift is not null
            && !string.IsNullOrWhiteSpace(
                databaseShift.Name))
        {
            return databaseShift.Name;
        }

        return FirstNonEmpty(
                   ReadProperty(
                       row,
                       "ShiftName"),
                   ReadProperty(
                       row,
                       "Shift"),
                   ReadProperty(
                       row,
                       "ShiftCode"))
               ?? T(
                   "MES06.Shift.Unknown",
                   "No shift");
    }

    private void RemoveEmptyStateNoteColumn()
    {
        for (var index =
                 GridReport.Columns.Count - 1;
             index >= 0;
             index--)
        {
            if (GridReport.Columns[index]
                    is not DataGridTextColumn column
                || column.Binding
                    is not Binding binding)
            {
                continue;
            }

            var property =
                (binding.Path?.Path
                 ?? string.Empty)
                .Trim()
                .TrimStart('[')
                .TrimEnd(']');

            if (IsStateNoteProperty(
                    property)
                || IsStateNoteHeader(
                    Convert.ToString(
                        column.Header,
                        CultureInfo.CurrentCulture)
                    ?? string.Empty))
            {
                GridReport.Columns.RemoveAt(
                    index);
            }
        }
    }

    private static bool IsStateNoteProperty(
        string property)
    {
        return string.Equals(
                   property,
                   "Note",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   property,
                   "Notes",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   property,
                   "Comment",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   property,
                   "Remark",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   property,
                   "CustomText",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStateNoteHeader(
        string header)
    {
        var value =
            header.Trim();

        return value.Equals(
                   "Poznámka",
                   StringComparison.CurrentCultureIgnoreCase)
               || value.Equals(
                   "Note",
                   StringComparison.CurrentCultureIgnoreCase)
               || value.Equals(
                   "Comment",
                   StringComparison.CurrentCultureIgnoreCase)
               || value.Equals(
                   "Bemerkung",
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void BuildStateDowntimeSummary()
    {
        var rows =
            _currentRows
                .ToList();

        if (rows.Count == 0)
        {
            CounterSummaryBorder.Visibility =
                Visibility.Collapsed;

            GridCounterSummary.ItemsSource =
                null;

            return;
        }

        var distinctWorkcenters =
            rows
                .Select(
                    ResolveStateWorkcenter)
                .Where(code =>
                    !string.IsNullOrWhiteSpace(
                        code))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var splitByWorkcenter =
            distinctWorkcenters.Count > 1;

        var prepared =
            rows
                .Select(row =>
                    new
                    {
                        Workcenter =
                            ResolveStateWorkcenter(
                                row),
                        Category =
                            ResolveStateCategory(
                                row),
                        State =
                            ResolveStateName(
                                row),
                        Occurrences =
                            ResolveStateOccurrenceCount(
                                row),
                        DurationSeconds =
                            ResolveStateDurationSeconds(
                                row)
                    })
                .Where(item =>
                    item.DurationSeconds > 0d
                    || item.Occurrences > 0)
                .ToList();

        var durationByWorkcenter =
            prepared
                .GroupBy(item =>
                    splitByWorkcenter
                        ? item.Workcenter
                        : string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.Sum(item =>
                            item.DurationSeconds),
                    StringComparer.OrdinalIgnoreCase);

        var summary =
            prepared
                .GroupBy(item =>
                    new
                    {
                        Workcenter =
                            splitByWorkcenter
                                ? item.Workcenter
                                : string.Empty,
                        item.Category,
                        item.State
                    })
                .Select(group =>
                {
                    var totalSeconds =
                        group.Sum(item =>
                            item.DurationSeconds);

                    var denominator =
                        durationByWorkcenter.TryGetValue(
                            group.Key.Workcenter,
                            out var workcenterDuration)
                            ? workcenterDuration
                            : 0d;

                    return new Mes06StateSummaryRow
                    {
                        WorkcenterCode =
                            group.Key.Workcenter,
                        CategoryName =
                            group.Key.Category,
                        StateName =
                            group.Key.State,
                        Occurrences =
                            group.Sum(item =>
                                item.Occurrences),
                        DurationSeconds =
                            totalSeconds,
                        DurationText =
                            FormatStateDuration(
                                totalSeconds),
                        SharePercent =
                            denominator > 0d
                                ? totalSeconds
                                  / denominator
                                  * 100d
                                : 0d
                    };
                })
                .OrderBy(row =>
                    row.WorkcenterCode,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(row =>
                    row.DurationSeconds)
                .ThenBy(row =>
                    row.CategoryName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row =>
                    row.StateName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        GridCounterSummary.Columns.Clear();

        if (splitByWorkcenter)
        {
            AddStateSummaryColumn(
                T(
                    "MES06.StateSummary.Column.Workcenter",
                    "Workcenter"),
                nameof(Mes06StateSummaryRow.WorkcenterCode),
                115);
        }

        AddStateSummaryColumn(
            T(
                "MES06.StateSummary.Column.Category",
                "Category"),
            nameof(Mes06StateSummaryRow.CategoryName),
            190);

        AddStateSummaryColumn(
            T(
                "MES06.StateSummary.Column.State",
                "State"),
            nameof(Mes06StateSummaryRow.StateName),
            260);

        AddStateSummaryColumn(
            T(
                "MES06.StateSummary.Column.Count",
                "Count"),
            nameof(Mes06StateSummaryRow.Occurrences),
            90,
            "N0");

        AddStateSummaryColumn(
            T(
                "MES06.StateSummary.Column.Duration",
                "Total duration"),
            nameof(Mes06StateSummaryRow.DurationText),
            125);

        AddStateSummaryColumn(
            T(
                "MES06.StateSummary.Column.Share",
                "Share"),
            nameof(Mes06StateSummaryRow.SharePercent),
            95,
            "N1",
            " %");

        GridCounterSummary.ItemsSource =
            summary;

        TxtCounterSummaryTitle.Text =
            T(
                "MES06.StateSummary.Title",
                "Downtime and state summary");

        CounterSummaryBorder.Visibility =
            summary.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void AddStateSummaryColumn(
        string header,
        string property,
        double width,
        string? format = null,
        string? suffix = null)
    {
        var binding =
            new Binding(
                property)
            {
                Mode =
                    BindingMode.OneWay
            };

        if (!string.IsNullOrWhiteSpace(
                format))
        {
            binding.StringFormat =
                string.IsNullOrWhiteSpace(
                    suffix)
                    ? format
                    : $"{{0:{format}}}{suffix}";
        }

        GridCounterSummary.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    header,
                Binding =
                    binding,
                Width =
                    new DataGridLength(
                        width)
            });
    }

    private static string ResolveStateWorkcenter(
        object row)
    {
        return FirstNonEmpty(
                   ReadProperty(
                       row,
                       "WorkcenterCode"),
                   ReadProperty(
                       row,
                       "Workcenter"),
                   ReadProperty(
                       row,
                       "ResourceCode"),
                   ReadProperty(
                       row,
                       "Resource"))
               ?? string.Empty;
    }

    private static string ResolveStateCategory(
        object row)
    {
        return FirstNonEmpty(
                   ReadProperty(
                       row,
                       "CategoryName"),
                   ReadProperty(
                       row,
                       "StateCategory"),
                   ReadProperty(
                       row,
                       "Category"))
               ?? string.Empty;
    }

    private static string ResolveStateName(
        object row)
    {
        return FirstNonEmpty(
                   ReadProperty(
                       row,
                       "StateName"),
                   ReadProperty(
                       row,
                       "State"),
                   ReadProperty(
                       row,
                       "Name"),
                   ReadProperty(
                       row,
                       "Description"))
               ?? string.Empty;
    }

    private static int ResolveStateOccurrenceCount(
        object row)
    {
        var raw =
            ReadProperty(
                row,
                "Count");

        if (raw is not null)
        {
            try
            {
                var count =
                    Convert.ToInt32(
                        raw,
                        CultureInfo.InvariantCulture);

                if (count > 0)
                {
                    return count;
                }
            }
            catch
            {
                // One state interval still represents one occurrence.
            }
        }

        return 1;
    }

    private static double ResolveStateDurationSeconds(
        object row)
    {
        var start =
            FirstDateTime(
                ReadProperty(
                    row,
                    "From"),
                ReadProperty(
                    row,
                    "Starttime"),
                ReadProperty(
                    row,
                    "StartTime"));

        var end =
            FirstDateTime(
                ReadProperty(
                    row,
                    "To"),
                ReadProperty(
                    row,
                    "Endtime"),
                ReadProperty(
                    row,
                    "EndTime"));

        if (start.HasValue
            && end.HasValue
            && end.Value >= start.Value)
        {
            return (
                end.Value
                - start.Value)
                .TotalSeconds;
        }

        foreach (var property
                 in new[]
                 {
                     "DurationSeconds",
                     "StateDurationSeconds",
                     "Duration"
                 })
        {
            var raw =
                ReadProperty(
                    row,
                    property);

            if (raw is TimeSpan timeSpan)
            {
                return Math.Max(
                    0d,
                    timeSpan.TotalSeconds);
            }

            if (raw is null)
            {
                continue;
            }

            try
            {
                return Math.Max(
                    0d,
                    Convert.ToDouble(
                        raw,
                        CultureInfo.InvariantCulture));
            }
            catch
            {
                // Try the next compatible property.
            }
        }

        return 0d;
    }

    private static string FormatStateDuration(
        double seconds)
    {
        if (seconds <= 0d)
        {
            return "00:00:00";
        }

        var span =
            TimeSpan.FromSeconds(
                seconds);

        if (span.TotalDays >= 1d)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}d {1:00}:{2:00}:{3:00}",
                (int)span.TotalDays,
                span.Hours,
                span.Minutes,
                span.Seconds);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            "{0:00}:{1:00}:{2:00}",
            (int)span.TotalHours,
            span.Minutes,
            span.Seconds);
    }
}
