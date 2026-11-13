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
    private void ExportMesordV6(bool pdf)
    {
        if (!_dataLoaded) return;
        var ext = pdf ? ".pdf" : ".xlsx";
        var dialog = new SaveFileDialog
        {
            DefaultExt = ext,
            Filter = pdf ? "PDF dokument (*.pdf)|*.pdf" : "Excel (*.xlsx)|*.xlsx",
            FileName = $"MESORD_{_orderNumber}_{DateTime.Now:yyyyMMdd-HHmm}{ext}"
        };
        if (dialog.ShowDialog() != true) return;
        var operationId = DMS.Desktop.Logging.DmsLogger.NewOperationId();
        var format = pdf ? "PDF" : "XLSX";
        _logger.Info($"MESORD EXPORT_START: OperationId={operationId}; Order={_orderNumber}; " +
            $"Format={format}; ShiftDay={_selectedDay:yyyy-MM-dd}; User={AuthorV63}");
        try
        {
            var data = BuildDataV6(_operationSelector.SelectedItem as OperationChoice);
            if (pdf) WritePdfV63(dialog.FileName, data);
            else WriteExcelV63(dialog.FileName, data);
            var operation = _operationSelector.SelectedItem as OperationChoice;
            _logger.Info($"MESORD EXPORT_OK: OperationId={operationId}; Format={format}; Order={_orderNumber}; " +
                $"Operation={operation?.Code}; ShiftDay={_selectedDay:yyyy-MM-dd}; " +
                $"Shifts={data.Windows.Count}; States={data.States.Count}; " +
                $"Operators={data.Operators.Count}; File={dialog.FileName}; User={AuthorV63}");
            OfferOpenMesordExportV61(dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error($"MESORD EXPORT_FAILED: OperationId={operationId}; Format={format}; " +
                $"Order={_orderNumber}; File={dialog.FileName}; User={AuthorV63}", ex);
            DMS.Desktop.UI.DmsMessage.Error("MESORD", MesordText("Export se nepodařil:") + "\n" + ex.Message, Window.GetWindow(this));
        }
    }


    private void OfferOpenMesordExportV61(string path)
    {
        if (!File.Exists(path)) return;
        if (!DmsMessage.Confirm(MesordText("MESORD – export"),
                MesordText("Soubor byl úspěšně vytvořen:") + "\n" + path + "\n\n" + MesordText("Chceš jej nyní otevřít?"),
                Window.GetWindow(this))) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error("MESORD exported file could not be opened.", ex);
            DmsMessage.Error("MESORD", MesordText("Soubor se nepodařilo otevřít:") + "\n" + ex.Message, Window.GetWindow(this));
        }
    }
}
