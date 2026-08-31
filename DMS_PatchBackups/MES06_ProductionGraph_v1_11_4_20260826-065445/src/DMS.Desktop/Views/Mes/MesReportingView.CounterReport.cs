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

        AddCounterWorkersColumn();

        BuildCounterSummary();
    }

    private sealed class Mes06CounterSummaryRow
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string CounterName { get; init; } = string.Empty;
        public string CounterKind { get; init; } = string.Empty;
        public string UserText { get; init; } = string.Empty;
        public decimal Value { get; init; }
    }

    private void BuildCounterSummary()
    {
        if (!_mes06CounterReportMode)
        {
            CounterSummaryBorder.Visibility =
                Visibility.Collapsed;

            GridCounterSummary.ItemsSource =
                null;

            return;
        }

        var rows =
            _currentRows
                .OfType<Mes06CounterReportRecord>()
                .ToList();

        if (rows.Count == 0)
        {
            CounterSummaryBorder.Visibility =
                Visibility.Collapsed;

            GridCounterSummary.ItemsSource =
                null;

            return;
        }

        var selectedWorkcenters =
            rows
                .Select(row =>
                    row.WorkcenterCode)
                .Where(code =>
                    !string.IsNullOrWhiteSpace(
                        code))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(code =>
                    code,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        var splitByWorkcenter =
            selectedWorkcenters.Count > 1;

        var summary =
            rows
                .Where(row =>
                    row.Value.HasValue)
                .GroupBy(row =>
                    new
                    {
                        Workcenter =
                            splitByWorkcenter
                                ? row.WorkcenterCode
                                : string.Empty,
                        Counter =
                            row.CounterName,
                        Kind =
                            row.CounterKind,
                        // Keep user-text categories separate when FASTEC
                        // stores a meaning below a generic Chyba 1..9 counter.
                        UserText =
                            string.IsNullOrWhiteSpace(
                                row.CustomText)
                                ? string.Empty
                                : row.CustomText.Trim()
                    })
                .Select(group =>
                    new Mes06CounterSummaryRow
                    {
                        WorkcenterCode =
                            group.Key.Workcenter,
                        CounterName =
                            group.Key.Counter,
                        CounterKind =
                            group.Key.Kind,
                        UserText =
                            group.Key.UserText,
                        Value =
                            group.Sum(row =>
                                row.Value
                                ?? 0m)
                    })
                .OrderBy(row =>
                    row.WorkcenterCode,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row =>
                    row.CounterKind,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row =>
                    row.CounterName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row =>
                    row.UserText,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        GridCounterSummary.Columns.Clear();

        if (splitByWorkcenter)
        {
            AddCounterSummaryColumn(
                T(
                    "MES06.CounterSummary.Column.Workcenter",
                    "Workcenter"),
                nameof(Mes06CounterSummaryRow.WorkcenterCode),
                115);
        }

        AddCounterSummaryColumn(
            T(
                "MES06.CounterSummary.Column.Counter",
                "Counter"),
            nameof(Mes06CounterSummaryRow.CounterName),
            200);

        AddCounterSummaryColumn(
            T(
                "MES06.CounterSummary.Column.Kind",
                "Kind"),
            nameof(Mes06CounterSummaryRow.CounterKind),
            100);

        if (summary.Any(row =>
                !string.IsNullOrWhiteSpace(
                    row.UserText)))
        {
            AddCounterSummaryColumn(
                T(
                    "MES06.CounterSummary.Column.UserText",
                    "User text"),
                nameof(Mes06CounterSummaryRow.UserText),
                240);
        }

        AddCounterSummaryColumn(
            T(
                "MES06.CounterSummary.Column.Value",
                "Total value"),
            nameof(Mes06CounterSummaryRow.Value),
            120,
            "N0");

        GridCounterSummary.ItemsSource =
            summary;

        TxtCounterSummaryTitle.Text =
            T(
                "MES06.CounterSummary.Title",
                "Counter summary");

        CounterSummaryBorder.Visibility =
            summary.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void AddCounterSummaryColumn(
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

    private void AddCounterWorkersColumn()
    {
        var template =
            new DataTemplate();

        var itemsControl =
            new FrameworkElementFactory(
                typeof(ItemsControl));

        itemsControl.SetBinding(
            ItemsControl.ItemsSourceProperty,
            new Binding(
                "Workers")
            {
                Mode =
                    BindingMode.OneWay
            });

        var wrapPanelFactory =
            new FrameworkElementFactory(
                typeof(WrapPanel));

        var itemsPanel =
            new ItemsPanelTemplate(
                wrapPanelFactory);

        itemsControl.SetValue(
            ItemsControl.ItemsPanelProperty,
            itemsPanel);

        var itemTemplate =
            new DataTemplate();

        var itemPanel =
            new FrameworkElementFactory(
                typeof(StackPanel));

        itemPanel.SetValue(
            StackPanel.OrientationProperty,
            Orientation.Horizontal);

        var workerText =
            new FrameworkElementFactory(
                typeof(TextBlock));

        workerText.SetBinding(
            TextBlock.TextProperty,
            new Binding(
                "DisplayText")
            {
                Mode =
                    BindingMode.OneWay
            });

        var workerStyle =
            new Style(
                typeof(TextBlock));

        workerStyle.Setters.Add(
            new Setter(
                TextBlock.FontWeightProperty,
                FontWeights.Normal));

        var mainWorkerTrigger =
            new DataTrigger
            {
                Binding =
                    new Binding(
                        "IsMainWorker"),
                Value =
                    true
            };

        mainWorkerTrigger.Setters.Add(
            new Setter(
                TextBlock.FontWeightProperty,
                FontWeights.Bold));

        workerStyle.Triggers.Add(
            mainWorkerTrigger);

        workerText.SetValue(
            FrameworkElement.StyleProperty,
            workerStyle);

        var separatorText =
            new FrameworkElementFactory(
                typeof(TextBlock));

        separatorText.SetBinding(
            TextBlock.TextProperty,
            new Binding(
                "Separator")
            {
                Mode =
                    BindingMode.OneWay
            });

        itemPanel.AppendChild(
            workerText);

        itemPanel.AppendChild(
            separatorText);

        itemTemplate.VisualTree =
            itemPanel;

        itemsControl.SetValue(
            ItemsControl.ItemTemplateProperty,
            itemTemplate);

        template.VisualTree =
            itemsControl;

        GridReport.Columns.Add(
            new DataGridTemplateColumn
            {
                Header =
                    T(
                        "MES06.Counter.Column.Operators",
                        "Operators"),
                CellTemplate =
                    template,
                Width =
                    new DataGridLength(
                        330d)
            });
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
                    T("MES06.Counter.Column.UserText", "User text"),
                    T("MES06.Counter.Column.Operators", "Operators")
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
                worksheet.Cell(excelRow, 13).Value = row.WorkersDisplay;
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
