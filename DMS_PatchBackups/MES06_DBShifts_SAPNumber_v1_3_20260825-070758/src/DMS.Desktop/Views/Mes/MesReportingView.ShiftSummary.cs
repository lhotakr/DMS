using DMS.Integration.Mes.Reporting.Definitions;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class Mes06GridRow
    {
        private readonly object? _source;
        private readonly Dictionary<string, object?> _values =
            new(StringComparer.OrdinalIgnoreCase);

        public Mes06GridRow(
            object source,
            string shiftLabel)
        {
            _source = source;
            ShiftLabel = shiftLabel;
        }

        public Mes06GridRow(
            string shiftLabel,
            bool isGrandTotal)
        {
            ShiftLabel = shiftLabel;
            IsSummary = true;
            IsGrandTotal = isGrandTotal;
        }

        public string ShiftLabel { get; }
        public bool IsSummary { get; }
        public bool IsGrandTotal { get; }

        public object? this[string propertyName]
        {
            get
            {
                if (string.Equals(
                        propertyName,
                        "ShiftName",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ShiftLabel;
                }

                if (_values.TryGetValue(
                        propertyName,
                        out var value))
                {
                    return value;
                }

                return _source is null
                    ? null
                    : ReadProperty(
                        _source,
                        propertyName);
            }
            set =>
                _values[propertyName] =
                    value;
        }
    }

    private sealed record ShiftBucketKey(
        string Name,
        DateTime? ShiftStart,
        DateTime SortTime)
    {
        public string DisplayText =>
            ShiftStart.HasValue
                ? $"{Name} · {ShiftStart:dd.MM.yyyy}"
                : Name;
    }

    private static readonly HashSet<string> Mes06SummedProperties =
        new(
            new[]
            {
                "Total",
                "Good",
                "Bad",
                "Rework",
                "UtilizationSeconds",
                "DowntimeSeconds",

                // Compatible fallback names if a report DTO exposes raw FASTEC
                // metric names instead of the friendly reporting aliases.
                "PerformanceTotal",
                "PerformanceGood",
                "PerformanceBad",
                "DurationUtilization",
                "DurationDown"
            },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applies MES06 grid-only presentation. Raw _currentRows stay untouched
    /// for charts, KPI calculation and Excel export.
    /// </summary>
    private void ApplyGridPresentation(
        MesReportDefinition definition)
    {
        BuildColumns(
            definition);

        if (!IsProductionReport(
                definition))
        {
            GridReport.ItemsSource =
                _currentRows;

            return;
        }

        RebindDynamicColumnsForDisplayRows(
            definition);

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
                        150d)
            });

        GridReport.ItemsSource =
            BuildProductionDisplayRows(
                definition,
                _currentRows);
    }

    private static bool IsProductionReport(
        MesReportDefinition definition)
    {
        // The existing MES06 dispatch treats every data source except States
        // and Counters as production.
        return !string.Equals(
                   definition.DataSource,
                   "States",
                   StringComparison.OrdinalIgnoreCase)
               && !string.Equals(
                   definition.DataSource,
                   "Counters",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void RebindDynamicColumnsForDisplayRows(
        MesReportDefinition definition)
    {
        var definitionColumns =
            definition.Columns
                .Where(column =>
                    !string.IsNullOrWhiteSpace(
                        column.Property))
                .ToList();

        var actualColumns =
            GridReport.Columns
                .OfType<DataGridTextColumn>()
                .ToList();

        var count =
            Math.Min(
                definitionColumns.Count,
                actualColumns.Count);

        for (var index = 0;
             index < count;
             index++)
        {
            var definitionColumn =
                definitionColumns[index];

            actualColumns[index].Binding =
                new Binding(
                    $"[{definitionColumn.Property}]")
                {
                    Mode =
                        BindingMode.OneWay,
                    StringFormat =
                        string.IsNullOrWhiteSpace(
                            definitionColumn.Format)
                            ? null
                            : definitionColumn.Format
                };
        }
    }

    private IReadOnlyList<Mes06GridRow> BuildProductionDisplayRows(
        MesReportDefinition definition,
        IReadOnlyList<object> rows)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<Mes06GridRow>();
        }

        var buckets =
            rows
                .Select(row =>
                    new
                    {
                        Row = row,
                        Bucket =
                            ResolveShiftBucket(
                                row)
                    })
                .GroupBy(
                    item => item.Bucket)
                .OrderBy(group =>
                    group.Key.SortTime)
                .ToList();

        var displayRows =
            new List<Mes06GridRow>(
                rows.Count
                + buckets.Count
                + 1);

        foreach (var bucket
                 in buckets)
        {
            foreach (var item
                     in bucket
                         .OrderBy(entry =>
                             ResolveRowStart(
                                 entry.Row)))
            {
                displayRows.Add(
                    new Mes06GridRow(
                        item.Row,
                        bucket.Key.DisplayText));
            }

            displayRows.Add(
                CreateSummaryRow(
                    definition,
                    bucket
                        .Select(item =>
                            item.Row)
                        .ToList(),
                    string.Format(
                        CultureInfo.CurrentCulture,
                        T(
                            "MES06.Summary.ShiftTotal",
                            "Shift total: {0}"),
                        bucket.Key.DisplayText),
                    isGrandTotal: false));
        }

        displayRows.Add(
            CreateSummaryRow(
                definition,
                rows,
                T(
                    "MES06.Summary.GrandTotal",
                    "TOTAL"),
                isGrandTotal: true));

        return displayRows;
    }

    private Mes06GridRow CreateSummaryRow(
        MesReportDefinition definition,
        IEnumerable<object> rows,
        string label,
        bool isGrandTotal)
    {
        var materialized =
            rows.ToList();

        var summary =
            new Mes06GridRow(
                label,
                isGrandTotal);

        foreach (var column
                 in definition.Columns)
        {
            if (string.IsNullOrWhiteSpace(
                    column.Property)
                || !Mes06SummedProperties.Contains(
                    column.Property))
            {
                continue;
            }

            decimal sum = 0m;
            var hasValue = false;

            foreach (var row
                     in materialized)
            {
                if (!TryToDecimal(
                        ReadProperty(
                            row,
                            column.Property),
                        out var numeric))
                {
                    continue;
                }

                sum += numeric;
                hasValue = true;
            }

            if (hasValue)
            {
                summary[column.Property] =
                    sum;
            }
        }

        return summary;
    }

    private ShiftBucketKey ResolveShiftBucket(
        object row)
    {
        var shiftName =
            FirstNonEmpty(
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

        var shiftStart =
            FirstDateTime(
                ReadProperty(
                    row,
                    "ShiftStart"),
                ReadProperty(
                    row,
                    "ShiftStarttime"),
                ReadProperty(
                    row,
                    "ShiftStartTime"));

        var rowStart =
            ResolveRowStart(
                row);

        // If the DTO does not expose ShiftStart explicitly, the report still
        // remains separated by calendar day + shift name rather than merging
        // every morning shift in a multi-day report into one bucket.
        shiftStart ??=
            rowStart == DateTime.MinValue
                ? null
                : rowStart.Date;

        return new ShiftBucketKey(
            shiftName,
            shiftStart,
            shiftStart
            ?? rowStart);
    }

    private static DateTime ResolveRowStart(
        object row)
    {
        return FirstDateTime(
                   ReadProperty(
                       row,
                       "From"),
                   ReadProperty(
                       row,
                       "Starttime"),
                   ReadProperty(
                       row,
                       "StartTime"))
               ?? DateTime.MinValue;
    }

    private static string? FirstNonEmpty(
        params object?[] values)
    {
        foreach (var value
                 in values)
        {
            var text =
                Convert.ToString(
                    value,
                    CultureInfo.CurrentCulture);

            if (!string.IsNullOrWhiteSpace(
                    text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static DateTime? FirstDateTime(
        params object?[] values)
    {
        foreach (var value
                 in values)
        {
            switch (value)
            {
                case DateTime dateTime:
                    return dateTime;

                case DateTimeOffset offset:
                    return offset.DateTime;
            }

            if (DateTime.TryParse(
                    Convert.ToString(
                        value,
                        CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryToDecimal(
        object? value,
        out decimal number)
    {
        number = 0m;

        if (value is null)
        {
            return false;
        }

        try
        {
            number =
                Convert.ToDecimal(
                    value,
                    CultureInfo.InvariantCulture);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void GridReport_LoadingRow(
        object sender,
        DataGridRowEventArgs e)
    {
        ResetSummaryRowAppearance(
            e.Row);

        if (e.Row.Item
            is not Mes06GridRow item
            || !item.IsSummary)
        {
            return;
        }

        e.Row.FontWeight =
            FontWeights.Bold;

        if (item.IsGrandTotal)
        {
            e.Row.SetResourceReference(
                Control.BackgroundProperty,
                "DmsAccentBrush");

            e.Row.SetResourceReference(
                Control.ForegroundProperty,
                "DmsOnAccentBrush");

            return;
        }

        e.Row.SetResourceReference(
            Control.BackgroundProperty,
            "DmsBackgroundBrush");

        e.Row.SetResourceReference(
            Control.ForegroundProperty,
            "DmsForegroundBrush");
    }

    private void GridReport_UnloadingRow(
        object sender,
        DataGridRowEventArgs e)
    {
        ResetSummaryRowAppearance(
            e.Row);
    }

    private static void ResetSummaryRowAppearance(
        DataGridRow row)
    {
        row.ClearValue(
            Control.BackgroundProperty);

        row.ClearValue(
            Control.ForegroundProperty);

        row.ClearValue(
            Control.FontWeightProperty);
    }
}
