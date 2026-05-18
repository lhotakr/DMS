using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapImportView : UserControl
{
    public SapImportView()
    {
        InitializeComponent();
    }

    private void BtnSelectMara_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SelectExcelFile("Vyber MARA export");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            TxtMaraFile.Text = filePath;
        }
    }

    private void BtnSelectMakt_Click(object sender, RoutedEventArgs e)
    {
        var filePath = SelectExcelFile("Vyber MAKT export");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            TxtMaktFile.Text = filePath;
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            storagePaths.EnsureDirectories();

            var rangesPath = storagePaths.MaterialRangesFilePath;
            var outputPath = storagePaths.SapMaterialsFilePath;

            if (!File.Exists(rangesPath))
            {
                TxtResult.Text =
                    "Import zastaven.\n\n" +
                    "Nenalezen konfigurační soubor číselných okruhů:\n" +
                    rangesPath;
                return;
            }

            var ranges = new SapMaterialRangeLoader().LoadFromJson(rangesPath);

            if (ranges.Count == 0)
            {
                TxtResult.Text =
                    "Import zastaven.\n\n" +
                    "Nenačetly se žádné SAP číselné okruhy:\n" +
                    rangesPath;
                return;
            }

            var classifier = new SapMaterialClassifier(ranges);
            var repository = new JsonSapMaterialRepository(outputPath);

            var service = new SapMaterialExcelImportService(
                classifier,
                repository);

            var result = service.Import(
                TxtMaraFile.Text,
                TxtMaktFile.Text);

            TxtResult.Text =
                result.ToDisplayText() +
                "\n\nVýstupní soubor:\n" +
                outputPath;
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Import selhal.\n\n" +
                ex.Message;
        }
        if (!File.Exists(TxtMaraFile.Text))
        {
            MessageBox.Show(
                "Nejdřív vyber platný MARA Excel.",
                "SAP00",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(TxtMaktFile.Text))
        {
            MessageBox.Show(
                "Nejdřív vyber platný MAKT Excel.",
                "SAP00",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            storagePaths.EnsureDirectories();

            var rangesPath = storagePaths.MaterialRangesFilePath;
            var outputPath = storagePaths.SapMaterialsFilePath;

            var ranges = new SapMaterialRangeLoader().LoadFromJson(rangesPath);
            var classifier = new SapMaterialClassifier(ranges);
            var repository = new JsonSapMaterialRepository(outputPath);

            var service = new SapMaterialExcelImportService(
                classifier,
                repository);

            var result = service.Import(
                TxtMaraFile.Text,
                TxtMaktFile.Text);

            TxtResult.Text = result.ToDisplayText();
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Import selhal.\n\n" +
                ex.Message;
        }
    }

    private static string? SelectExcelFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Excel soubory (*.xlsx)|*.xlsx|Všechny soubory (*.*)|*.*"
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}