using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapBomImportView : UserControl
{
    private readonly string _cachePath;
    private const int PreviewRowLimit = 500;

    public SapBomImportView()
    {
        InitializeComponent();

        var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
        storagePaths.EnsureDirectories();

        _cachePath = Path.Combine(
            @"Z:\SAP\DMS-db\DEV",
            "Data",
            "sap-boms.json");

        DgvColumnMapping.ItemsSource = SapBomExcelColumnDefinitions.All;

        TxtResult.Text =
            "Připraveno k importu kusovníků.\n\n" +
            "Vyber exporty MAST, STKO a STPO ze SAPu.";

        LoadCachePreview(showEmptyMessage: false);
    }

    private void BtnSelectMast_Click(object sender, RoutedEventArgs e)
    {
        SelectFileIntoTextBox(TxtMastFile);
    }

    private void BtnSelectStko_Click(object sender, RoutedEventArgs e)
    {
        SelectFileIntoTextBox(TxtStkoFile);
    }

    private void BtnSelectStas_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtStasFile, "Vyber STAS export");
    }

    private void BtnSelectStpo_Click(object sender, RoutedEventArgs e)
    {
        SelectFileIntoTextBox(TxtStpoFile);
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }

        try
        {
            var repository = new JsonSapBomRepository(_cachePath);
            var service = new SapBomExcelImportService(repository);

            var result = service.Import(
                TxtMastFile.Text.Trim(),
                TxtStkoFile.Text.Trim(),
                TxtStasFile.Text.Trim(),
                TxtStpoFile.Text.Trim());

            TxtResult.Text =
                $"Import dokončen.\n\n" +
                $"MAST řádků: {result.MastRows}\n" +
                $"STKO řádků: {result.StkoRows}\n" +
                $"STPO řádků: {result.StpoRows}\n" +
                $"Importováno kusovníků: {result.ImportedBomCount}\n" +
                $"Importováno položek: {result.ImportedItemCount}\n" +
                $"Chyb: {result.ErrorRows}\n\n" +
                string.Join("\n", result.Messages);

            LoadCachePreview(showEmptyMessage: false);
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Import kusovníků selhal.\n\n" +
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
            var repository = new JsonSapBomRepository(_cachePath);
            var boms = repository.LoadAll();

            var rows = boms
                .OrderBy(item => item.MaterialNumber)
                .ThenBy(item => item.Plant)
                .ThenBy(item => item.BomNumber)
                .Take(500)
                .Select(item => new SapBomGridRow
                {
                    MaterialNumber = item.MaterialNumber,
                    Plant = item.Plant,
                    BomMeaning = item.BomMeaning,
                    BomUsage = item.BomUsage,
                    BomNumber = item.BomNumber,
                    Alternative = item.Alternative,
                    BaseQuantity = item.BaseQuantity?.ToString() ?? string.Empty,
                    BaseUnit = item.BaseUnit,
                    ItemCount = item.Items.Count
                })
                .ToList();

            DgvBoms.ItemsSource = rows;

            if (showEmptyMessage || rows.Count > 0)
            {
                TxtResult.Text =
                    boms.Count == 0
                        ? $"Zatím není načtená žádná cache kusovníků.\n\nOčekávaná cesta:\n{_cachePath}"
                        : $"Načtena cache kusovníků: {boms.Count} záznamů.\n" +
                          $"V tabulce je zobrazen pouze náhled prvních {rows.Count} záznamů.\n\n" +
                          _cachePath;
            }
        }
        catch (Exception ex)
        {
            TxtResult.Text =
                "Nepodařilo se načíst cache kusovníků.\n\n" +
                ex.Message;
        }
    }

    private bool ValidateInputFiles()
    {
        if (!File.Exists(TxtMastFile.Text.Trim()))
        {
            TxtResult.Text = "Vyber platný Excel export MAST.";
            return false;
        }

        if (!File.Exists(TxtStkoFile.Text.Trim()))
        {
            TxtResult.Text = "Vyber platný Excel export STKO.";
            return false;
        }
        if (!File.Exists(TxtStasFile.Text.Trim()))
        {
            TxtResult.Text = "Vyber platný Excel export STAS.";
            return false;
        }

        if (!File.Exists(TxtStpoFile.Text.Trim()))
        {
            TxtResult.Text = "Vyber platný Excel export STPO.";
            return false;
        }
        return true;

    }

    private static void SelectFileIntoTextBox(TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Vyber Excel export ze SAPu",
            Filter = "Excel soubory (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Všechny soubory (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FileName;
        }
    }
    private static void SelectExcelFileInto(TextBox targetTextBox, string title)
    {
        var filePath = SelectExcelFile(title);

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            targetTextBox.Text = filePath;
        }
    }

    private static string? SelectExcelFile(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "Excel soubory (*.xlsx)|*.xlsx|Všechny soubory (*.*)|*.*"
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }


    private sealed class SapBomGridRow
    {
        public string MaterialNumber { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
        public string BomMeaning { get; init; } = string.Empty;
        public string BomUsage { get; init; } = string.Empty;
        public string BomNumber { get; init; } = string.Empty;
        public string Alternative { get; init; } = string.Empty;
        public string BaseQuantity { get; init; } = string.Empty;
        public string BaseUnit { get; init; } = string.Empty;
        public int ItemCount { get; init; }
    }
}