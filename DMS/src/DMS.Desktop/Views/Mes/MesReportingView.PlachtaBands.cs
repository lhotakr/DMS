using ClosedXML.Excel;
using DMS.Integration.Mes.Reporting.Definitions;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private IReadOnlyList<object>? _plachtaBandSource;

    private readonly Dictionary<string, bool> _plachtaBandByWorkcenter =
        new(StringComparer.CurrentCultureIgnoreCase);

    private static readonly Color PlachtaAlternateBandColor =
        Color.FromRgb(
            231,
            230,
            230);

    private void EnsurePlachtaBandMap()
    {
        if (ReferenceEquals(
                _plachtaBandSource,
                _currentRows))
        {
            return;
        }

        _plachtaBandSource =
            _currentRows;

        _plachtaBandByWorkcenter.Clear();

        var workcenters =
            _currentRows
                .Select(row =>
                    Convert.ToString(
                        ReadProperty(
                            row,
                            "WorkcenterCode"),
                        CultureInfo.CurrentCulture)
                    ?.Trim()
                    ?? string.Empty)
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value))
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        for (var index = 0;
             index < workcenters.Count;
             index++)
        {
            _plachtaBandByWorkcenter[
                workcenters[index]] =
                index % 2 == 1;
        }
    }

    private bool TryGetPlachtaBand(
        object row,
        out bool shaded)
    {
        EnsurePlachtaBandMap();

        var workcenter =
            Convert.ToString(
                ReadProperty(
                    row,
                    "WorkcenterCode"),
                CultureInfo.CurrentCulture)
            ?.Trim()
            ?? string.Empty;

        return _plachtaBandByWorkcenter.TryGetValue(
            workcenter,
            out shaded);
    }

    private void ApplyPlachtaExcelBands(
        IXLWorksheet worksheet,
        MesReportDefinition definition)
    {
        if (!IsPlachtaReport(
                definition)
            || _currentRows.Count == 0)
        {
            return;
        }

        EnsurePlachtaBandMap();

        string previousWorkcenter =
            string.Empty;

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

            var excelRow =
                rowIndex + 2;

            var range =
                worksheet.Range(
                    excelRow,
                    1,
                    excelRow,
                    definition.Columns.Count);

            if (TryGetPlachtaBand(
                    row,
                    out var shaded)
                && shaded)
            {
                range.Style.Fill.BackgroundColor =
                    XLColor.FromHtml(
                        "#E7E6E6");
            }
            else
            {
                range.Style.Fill.BackgroundColor =
                    XLColor.White;
            }

            if (groupChanged)
            {
                range.Style.Border.TopBorder =
                    XLBorderStyleValues.Thin;

                range.Style.Border.TopBorderColor =
                    XLColor.Gray;
            }

            previousWorkcenter =
                workcenter;
        }
    }

    private void ApplyPrintablePlachtaBand(
        object row,
        TableRow tableRow)
    {
        if (!TryGetPlachtaBand(
                row,
                out var shaded))
        {
            return;
        }

        tableRow.Background =
            shaded
                ? new SolidColorBrush(
                    PlachtaAlternateBandColor)
                : Brushes.White;

        tableRow.Foreground =
            Brushes.Black;
    }
}