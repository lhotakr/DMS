using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapRoutingImportView : UserControl
{
    private const int PreviewRowLimit = 500;

    private readonly string _cachePath;

    public SapRoutingImportView()
    {
        InitializeComponent();

        var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
        storagePaths.EnsureDirectories();

        _cachePath = Path.Combine(
            @"Z:\SAP\DMS-db\DEV",
            "Data",
            "sap-routings.json");

        DgvColumnMapping.ItemsSource = SapRoutingExcelColumnDefinitions.All;

        TxtResult.Text =
            "Připraveno k importu pracovních postupů.\n\n" +
            "Vyber exporty MAPL, PLKO a PLPO ze SAPu.";

        LoadCachePreview(showEmptyMessage: false);
    }

    private void BtnSelectMapl_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtMaplFile, "Vyber MAPL export");
    }

    private void BtnSelectPlko_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtPlkoFile, "Vyber PLKO export");
    }

    private void BtnSelectPlpo_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtPlpoFile, "Vyber PLPO export");
    }
    private void BtnSelectPlas_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtPlasFile, "Vyber PLAS export");
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }
        
        try
        {
            var repository = new JsonSapRoutingRepository(_cachePath);
            var service = new SapRoutingExcelImportService(repository);

            var result = service.Import(
                TxtMaplFile.Text.Trim(),
                TxtPlkoFile.Text.Trim(),
                TxtPlasFile.Text.Trim(),
                TxtPlpoFile.Text.Trim());

            DgvRoutings.ItemsSource = null;

            TxtResult.Text =
                "Import pracovních postupů dokončen.\n\n" +
                $"MAPL řádků: {result.MaplRows}\n" +
                $"PLKO řádků: {result.PlkoRows}\n" +
                $"PLPO řádků: {result.PlpoRows}\n" +
                $"Importováno postupů: {result.ImportedRoutingCount}\n" +
                $"Importováno operací: {result.ImportedOperationCount}\n" +
                $"Přeskočeno alternativ PLNAL != 1: {result.SkippedAlternativeCount}\n" +
                $"Varování: {result.WarningCount}\n" +
                $"Chyb: {result.ErrorRows}\n\n" +
                string.Join("\n", result.Messages) +
                "\n\nVýstupní soubor:\n" +
                _cachePath +
                "\n\nNáhled nebyl automaticky načten kvůli velikosti dat. Použij tlačítko Načíst cache.";
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Import pracovních postupů selhal.\n\n" +
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
            var repository = new JsonSapRoutingRepository(_cachePath);
            var routings = repository.LoadAll();

            var rows = routings
                .OrderBy(item => item.MaterialNumber)
                .ThenBy(item => item.Plant)
                .ThenBy(item => item.GroupNumber)
                .Take(PreviewRowLimit)
                .Select(item => new SapRoutingGridRow
                {
                    MaterialNumber = item.MaterialNumber,
                    Plant = item.Plant,
                    RoutingMeaning = item.RoutingMeaning,
                    GroupNumber = item.GroupNumber,
                    Alternative = item.Alternative,
                    Description = item.Description,
                    OperationCount = item.Operations.Count,
                    WarningCount = item.ValidationMessages.Count,
                    HasCriticalError = item.HasCriticalError
                })
                .ToList();

            DgvRoutings.ItemsSource = rows;

            if (showEmptyMessage || rows.Count > 0)
            {
                TxtResult.Text =
                    routings.Count == 0
                        ? $"Zatím není načtená žádná cache pracovních postupů.\n\nOčekávaná cesta:\n{_cachePath}"
                        : $"Načtena cache pracovních postupů: {routings.Count} záznamů.\n" +
                          $"V tabulce je zobrazen pouze náhled prvních {rows.Count} záznamů.\n\n" +
                          _cachePath;
            }
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Nepodařilo se načíst cache pracovních postupů.\n\n" +
                ex.Message;
        }
    }

    private bool ValidateInputFiles()
    {
        if (!File.Exists(TxtMaplFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný MAPL Excel.",
                "SAP00 - Import pracovních postupů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (!File.Exists(TxtPlkoFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný PLKO Excel.",
                "SAP00 - Import pracovních postupů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (!File.Exists(TxtPlpoFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný PLPO Excel.",
                "SAP00 - Import pracovních postupů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (!File.Exists(TxtPlasFile.Text.Trim()))
        {
            MessageBox.Show(
                "Nejdřív vyber platný PLAS Excel.",
                "SAP00 - Import pracovních postupů",
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

    private sealed class SapRoutingGridRow
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
        public string RoutingMeaning { get; init; } = string.Empty;
        public string GroupNumber { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public int OperationCount { get; init; }
        public int WarningCount { get; init; }
        public bool HasCriticalError { get; init; }
    }
}