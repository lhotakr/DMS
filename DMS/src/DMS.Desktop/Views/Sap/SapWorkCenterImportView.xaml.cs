using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapWorkCenterImportView : UserControl
{
    private const int PreviewRowLimit = 500;

    private readonly string _cachePath;

    public SapWorkCenterImportView()
    {
        InitializeComponent();

        var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
        storagePaths.EnsureDirectories();

        _cachePath = Path.Combine(
            @"Z:\SAP\DMS-db\DEV",
            "Data",
            "sap-work-centers.json");

        DgvColumnMapping.ItemsSource = SapWorkCenterExcelColumnDefinitions.All;

        TxtResult.Text =
            "Připraveno k importu pracovišť.\n\n" +
            "Vyber exporty CRHD a CRTX ze SAPu.";

        LoadCachePreview(showEmptyMessage: false);
    }

    private void BtnSelectCrhd_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtCrhdFile, "Vyber CRHD export");
    }

    private void BtnSelectCrtx_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtCrtxFile, "Vyber CRTX export");
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }

        try
        {
            var repository = new JsonSapWorkCenterRepository(_cachePath);
            var service = new SapWorkCenterExcelImportService(repository);

            var result = service.Import(
                TxtCrhdFile.Text.Trim(),
                TxtCrtxFile.Text.Trim());

            DgvWorkCenters.ItemsSource = null;

            TxtResult.Text =
                "Import pracovišť dokončen.\n\n" +
                $"CRHD řádků: {result.CrhdRows}\n" +
                $"CRTX řádků: {result.CrtxRows}\n" +
                $"Importováno pracovišť: {result.ImportedWorkCenterCount}\n" +
                $"Importováno textů: {result.ImportedTextCount}\n" +
                $"Chyb: {result.ErrorRows}\n\n" +
                string.Join("\n", result.Messages) +
                "\n\nVýstupní soubor:\n" +
                _cachePath +
                "\n\nNáhled nebyl automaticky načten kvůli velikosti dat. Použij tlačítko Načíst cache.";
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Import pracovišť selhal.\n\n" +
                ex.Message;
        }
    }

    private void BtnLoadCache_Click(object sender, RoutedEventArgs e)
    {
        LoadCachePreview(showEmptyMessage: true);
    }

    private void LoadCachePreview(bool showEmptyMessage)
    {
        try
        {
            var repository = new JsonSapWorkCenterRepository(_cachePath);
            var workCenters = repository.LoadAll();

            var rows = workCenters
                .OrderBy(item => item.WorkCenter)
                .ThenBy(item => item.ObjectId)
                .Take(PreviewRowLimit)
                .Select(item => new SapWorkCenterGridRow
                {
                    ObjectId = item.ObjectId,
                    WorkCenter = item.WorkCenter,
                    Plant = item.Plant,
                    DisplayText = item.DisplayText,
                    TextCount = item.Texts.Count
                })
                .ToList();

            DgvWorkCenters.ItemsSource = rows;

            if (showEmptyMessage || rows.Count > 0)
            {
                TxtResult.Text =
                    workCenters.Count == 0
                        ? $"Zatím není načtená žádná cache pracovišť.\n\nOčekávaná cesta:\n{_cachePath}"
                        : $"Načtena cache pracovišť: {workCenters.Count} záznamů.\n" +
                          $"V tabulce je zobrazen pouze náhled prvních {rows.Count} záznamů.\n\n" +
                          _cachePath;
            }
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Nepodařilo se načíst cache pracovišť.\n\n" +
                ex.Message;
        }
    }

    private bool ValidateInputFiles()
    {
        if (!File.Exists(TxtCrhdFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný CRHD Excel.",
                "SAP00 - Import pracovišť",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (!File.Exists(TxtCrtxFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný CRTX Excel.",
                "SAP00 - Import pracovišť",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        return true;
    }

    private static void SelectExcelFileInto(TextBox target, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Excel soubory (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Všechny soubory (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private sealed class SapWorkCenterGridRow
    {
        public string ObjectId { get; init; } = string.Empty;
        public string WorkCenter { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public int TextCount { get; init; }
    }
}