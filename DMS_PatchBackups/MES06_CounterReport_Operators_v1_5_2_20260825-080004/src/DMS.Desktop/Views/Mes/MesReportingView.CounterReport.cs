using ClosedXML.Excel;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Reporting;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private void BuildCounterReportColumns()
    {
        GridReport.Columns.Clear();

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Shift",
                "Shift"),
            "ShiftName",
            145);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Timestamp",
                "Time"),
            "Timestamp",
            135,
            "dd.MM.yyyy HH:mm:ss");

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Workcenter",
                "Workcenter"),
            "WorkcenterCode",
            105);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Order",
                "Order"),
            "OrderCode",
            105);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Operation",
                "Operation"),
            "OperationCode",
            80);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Article",
                "Article"),
            "ProductCode",
            145);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.SapNumber",
                "SAP number"),
            "SapArticleNumber",
            115);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.OrderQuantity",
                "Order quantity"),
            "OrderQuantity",
            105,
            "N0");

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Counter",
                "Counter"),
            "CounterName",
            170);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Kind",
                "Kind"),
            "CounterKind",
            110);

        AddCounterColumn(
            T(
                "MES06.Counter.Column.Value",
                "Value"),
            "Value",
            95,
            "N0");

        AddCounterColumn(
            T(
                "MES06.Counter.Column.UserText",
                "User text"),
            "CustomText",
            240);
    }

    private void AddCounterColumn(
        string header,
        string property,
        double width,
        string? format = null)
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
                format;
        }

        GridReport.Columns.Add(
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

    private void ExportCounterReportExcel()
    {
        var rows =
            _currentRows
                .OfType<Mes06CounterReportRecord>()
                .ToList();

        if (rows.Count == 0)
        {
            DmsMessage.Show(
                T(
                    "MES06.Status.NoData",
                    "There is no report data to export."),
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog =
            new SaveFileDialog
            {
                Filter =
                    "Excel workbook (*.xlsx)|*.xlsx",
                FileName =
                    $"MES06_CounterReport_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var workbook =
                new XLWorkbook();

            var worksheet =
                workbook.Worksheets.Add(
                    "Counter report");

            var headers =
                new[]
                {
                    T("MES06.Counter.Column.Shift", "Shift"),
                    T("MES06.Counter.Column.Timestamp", "Time"),
                    T("MES06.Counter.Column.Workcenter", "Workcenter"),
                    T("MES06.Counter.Column.Order", "Order"),
                    T("MES06.Counter.Column.Operation", "Operation"),
                    T("MES06.Counter.Column.Article", "Article"),
                    T("MES06.Counter.Column.SapNumber", "SAP number"),
                    T("MES06.Counter.Column.OrderQuantity", "Order quantity"),
                    T("MES06.Counter.Column.Counter", "Counter"),
                    T("MES06.Counter.Column.Kind", "Kind"),
                    T("MES06.Counter.Column.Value", "Value"),
                    T("MES06.Counter.Column.UserText", "User text")
                };

            for (var column = 0;
                 column < headers.Length;
                 column++)
            {
                worksheet.Cell(
                        1,
                        column + 1)
                    .Value =
                    headers[column];
            }

            for (var index = 0;
                 index < rows.Count;
                 index++)
            {
                var row =
                    rows[index];

                var excelRow =
                    index + 2;

                worksheet.Cell(excelRow, 1).Value = row.ShiftName;
                worksheet.Cell(excelRow, 2).Value = row.Timestamp;
                worksheet.Cell(excelRow, 3).Value = row.WorkcenterCode;
                worksheet.Cell(excelRow, 4).Value = row.OrderCode;
                worksheet.Cell(excelRow, 5).Value = row.OperationCode;
                worksheet.Cell(excelRow, 6).Value = row.ProductCode;
                worksheet.Cell(excelRow, 7).Value = row.SapArticleNumber;

                if (row.OrderQuantity.HasValue)
                {
                    worksheet.Cell(excelRow, 8).Value = row.OrderQuantity.Value;
                }

                worksheet.Cell(excelRow, 9).Value = row.CounterName;
                worksheet.Cell(excelRow, 10).Value = row.CounterKind;

                if (row.Value.HasValue)
                {
                    worksheet.Cell(excelRow, 11).Value = row.Value.Value;
                }

                worksheet.Cell(excelRow, 12).Value = row.CustomText;
            }

            worksheet.Column(2).Style.DateFormat.Format =
                "dd.MM.yyyy HH:mm:ss";

            worksheet
                .ColumnsUsed()
                .AdjustToContents();

            workbook.SaveAs(
                dialog.FileName);

            _logger.AdminAction(
                "MES06",
                "ExportCounterReportExcel",
                _user,
                $"Rows={rows.Count}; File={dialog.FileName}");

            TxtStatus.Text =
                string.Format(
                    T(
                        "MES06.Status.Exported",
                        "Excel export created: {0}"),
                    dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 counter report Excel export failed.",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
