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
    private void BuildBonusReportColumns()
    {
        GridReport.Columns.Clear();

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.Order",
                "Zakázka"),
            "OrderCode",
            100);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.Product",
                "Artikl"),
            "ProductCode",
            150);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.SapNumber",
                "SAP číslo"),
            "SapNumber",
            120);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.Operation",
                "Operace"),
            "OperationCode",
            80);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.Workcenter",
                "Stroj"),
            "WorkcenterCode",
            105);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.Shift",
                "Směna"),
            "ShiftCode",
            80);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.Operator",
                "Pracovník"),
            "OperatorName",
            180);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.HumanCode",
                "Osobní číslo"),
            "HumanCode",
            105);

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.From",
                "Čas od"),
            "LoginFrom",
            135,
            "dd.MM.yyyy HH:mm");

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.To",
                "Čas do"),
            "LoginTo",
            135,
            "dd.MM.yyyy HH:mm");

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.NetShiftDurationMinutes",
                "Čistý čas na stroji [min]"),
            "NetShiftDurationMinutes",
            160,
            "N1");

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.GrossProduction",
                "StrojS hrubé"),
            "GrossProduction",
            120,
            "N0");

        AddBonusColumn(
            T(
                "MES06.BonusBase.Column.PrintedNet",
                "Natisknuto"),
            "PrintedNet",
            120,
            "N0");
    }

    private void AddBonusColumn(
        string header,
        string property,
        double width,
        string? format = null)
    {
        var binding =
            new Binding(property)
            {
                Mode = BindingMode.OneWay
            };

        if (!string.IsNullOrWhiteSpace(format))
        {
            binding.StringFormat = format;
        }

        GridReport.Columns.Add(
            new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = new DataGridLength(width)
            });
    }

    private void ExportBonusReportExcel()
    {
        var rows =
            _currentRows
                .OfType<MesBonusReportRecord>()
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
                    $"MES06_BonusReport_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
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
                    "Bonus report");

            var headers =
                new[]
                {
                    T("MES06.BonusBase.Column.Order", "Zakázka"),
                    T("MES06.BonusBase.Column.Product", "Artikl"),
                    T("MES06.BonusBase.Column.SapNumber", "SAP číslo"),
                    T("MES06.BonusBase.Column.Operation", "Operace"),
                    T("MES06.BonusBase.Column.Workcenter", "Stroj"),
                    T("MES06.BonusBase.Column.Shift", "Směna"),
                    T("MES06.BonusBase.Column.Operator", "Pracovník"),
                    T("MES06.BonusBase.Column.HumanCode", "Osobní číslo"),
                    T("MES06.BonusBase.Column.From", "Čas od"),
                    T("MES06.BonusBase.Column.To", "Čas do"),
                    T("MES06.BonusBase.Column.NetShiftDurationMinutes", "Čistý čas na stroji [min]"),
                    T("MES06.BonusBase.Column.GrossProduction", "StrojS hrubé"),
                    T("MES06.BonusBase.Column.PrintedNet", "Natisknuto")
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
                var row = rows[index];
                var excelRow = index + 2;

                worksheet.Cell(excelRow, 1).Value = row.OrderCode;
                worksheet.Cell(excelRow, 2).Value = row.ProductCode;
                worksheet.Cell(excelRow, 3).Value = row.SapNumber;
                worksheet.Cell(excelRow, 4).Value = row.OperationCode;
                worksheet.Cell(excelRow, 5).Value = row.WorkcenterCode;
                worksheet.Cell(excelRow, 6).Value = row.ShiftCode;
                worksheet.Cell(excelRow, 7).Value = row.OperatorName;
                worksheet.Cell(excelRow, 8).Value = row.HumanCode;
                worksheet.Cell(excelRow, 9).Value = row.LoginFrom;
                worksheet.Cell(excelRow, 10).Value = row.LoginTo;

                worksheet.Cell(excelRow, 11).Value = row.NetShiftDurationMinutes;
                worksheet.Cell(excelRow, 12).Value = row.GrossProduction;
                worksheet.Cell(excelRow, 13).Value = row.PrintedNet;
            }

            worksheet.Column(9).Style.DateFormat.Format =
                "dd.MM.yyyy HH:mm";

            worksheet.Column(10).Style.DateFormat.Format =
                "dd.MM.yyyy HH:mm";

            worksheet
                .ColumnsUsed()
                .AdjustToContents();

            workbook.SaveAs(dialog.FileName);

            _logger.AdminAction(
                "MES06",
                "ExportBonusReportExcel",
                _user,
                $"Rows={rows.Count}; File={dialog.FileName}");

            TxtStatus.Text =
                string.Format(
                    T(
                        "MES06.Status.Exported",
                        "Excel export created: {0}"),
                    dialog.FileName);

            OfferOpenExportedFile(
                dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 bonus report Excel export failed.",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MES06",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}