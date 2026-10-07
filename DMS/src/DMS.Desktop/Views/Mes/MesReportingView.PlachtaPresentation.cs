using ClosedXML.Excel;
using DMS.Integration.Mes.Reporting.Definitions;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class PlachtaHideZeroConverter
        : IValueConverter
    {
        public static readonly PlachtaHideZeroConverter Instance =
            new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            IsNumericZero(value)
                ? null
                : value;

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) =>
            Binding.DoNothing;
    }

    private static bool IsNumericZero(
        object? value) =>
        value switch
        {
            byte v => v == 0,
            sbyte v => v == 0,
            short v => v == 0,
            ushort v => v == 0,
            int v => v == 0,
            uint v => v == 0,
            long v => v == 0,
            ulong v => v == 0,
            float v => v == 0f,
            double v => v == 0d,
            decimal v => v == 0m,
            _ => false
        };

    private bool ShouldHidePlachtaZeroValue(
        MesReportDefinition definition,
        object? value) =>
        IsPlachtaReport(definition)
        && ChkHideZeros.IsChecked == true
        && IsNumericZero(value);

    private bool ShouldHideCurrentPlachtaZeroValue(
        object? value) =>
        CmbReport.SelectedItem
            is MesReportDefinition definition
        && ShouldHidePlachtaZeroValue(
            definition,
            value);

    private void ApplyPlachtaExcelPresentation(
        IXLWorksheet worksheet,
        MesReportDefinition definition)
    {
        if (!IsPlachtaReport(definition)
            || _currentRows.Count == 0)
        {
            return;
        }

        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.SheetView.FreezeRows(1);

        for (var columnIndex = 0;
             columnIndex < definition.Columns.Count;
             columnIndex++)
        {
            var definitionColumn =
                definition.Columns[columnIndex];

            if (string.IsNullOrWhiteSpace(
                    definitionColumn.Format))
            {
                continue;
            }

            var excelFormat =
                definitionColumn.Format.ToUpperInvariant() switch
                {
                    "N0" => "#,##0",
                    "N1" => "#,##0.0",
                    "N2" => "#,##0.00",
                    _ => string.Empty
                };

            if (string.IsNullOrWhiteSpace(
                    excelFormat))
            {
                continue;
            }

            worksheet
                .Range(
                    2,
                    columnIndex + 1,
                    _currentRows.Count + 1,
                    columnIndex + 1)
                .Style
                .NumberFormat
                .Format =
                excelFormat;
        }

        var wrapColumns =
            definition.Columns
                .Select(
                    (column, index) =>
                        new
                        {
                            column.Property,
                            Index = index + 1
                        })
                .Where(item =>
                    string.Equals(
                        item.Property,
                        "ProductDescription",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        item.Property,
                        "Notes",
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Index)
                .ToList();

        foreach (var columnIndex
                 in wrapColumns)
        {
            worksheet.Column(columnIndex)
                .Style
                .Alignment
                .WrapText = true;
        }

        string previousWorkcenter =
            string.Empty;

        var shaded = false;
        var firstGroup = true;

        for (var rowIndex = 0;
             rowIndex < _currentRows.Count;
             rowIndex++)
        {
            var row =
                _currentRows[rowIndex];

            var workcenter =
                Convert.ToString(
                    ReadProperty(
                        row,
                        "WorkcenterCode"),
                    CultureInfo.CurrentCulture)
                ?.Trim()
                ?? string.Empty;

            var groupChanged =
                !string.Equals(
                    workcenter,
                    previousWorkcenter,
                    StringComparison.CurrentCultureIgnoreCase);

            if (groupChanged)
            {
                if (!firstGroup)
                {
                    shaded = !shaded;
                }

                firstGroup = false;
                previousWorkcenter = workcenter;
            }

            var excelRow =
                rowIndex + 2;

            var rowRange =
                worksheet.Range(
                    excelRow,
                    1,
                    excelRow,
                    definition.Columns.Count);

            if (shaded)
            {
                rowRange
                    .Style
                    .Fill
                    .BackgroundColor =
                    XLColor.FromHtml(
                        "#E7E6E6");
            }
            else
            {
                rowRange
                    .Style
                    .Fill
                    .BackgroundColor =
                    XLColor.White;
            }

            if (groupChanged)
            {
                rowRange
                    .Style
                    .Border
                    .TopBorder =
                    XLBorderStyleValues.Thin;
            }
        }
    }
}
