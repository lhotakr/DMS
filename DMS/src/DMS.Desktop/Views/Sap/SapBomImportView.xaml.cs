using DMS.Core.Sap;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapBomImportView : UserControl
{
    private const int PreviewRowLimit = 500;

    private readonly string _cachePath;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly Action<string, string>? _logAction;

    public SapBomImportView()
        : this(new SapStoragePaths(@"Z:\SAP\DMS-db\DEV"))
    {
    }

    public SapBomImportView(
        SapStoragePaths storagePaths,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null,
        Action<string, string>? logAction = null)
    {
        InitializeComponent();

        _translate = translate;
        _translateFormat = translateFormat;
        _logAction = logAction;

        storagePaths.EnsureDirectories();
        _cachePath = Path.Combine(storagePaths.SapMirrorDirectory, "sap-boms.json");

        DgvColumnMapping.ItemsSource = SapBomExcelColumnDefinitions.All;

        ApplyLocalization();
        SetReadyMessage();
        LoadCachePreview(showEmptyMessage: false);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAP00.Bom.Title", "BOM import");
        TxtSubtitle.Text = T("SAP00.Bom.Summary", "Import SE16N exports of MAST, STKO, STAS and STPO into the local DMS cache. Alternative BOMs are split by STAS.");

        LblMast.Text = T("SAP00.Bom.Label.Mast", "MAST:");
        LblStko.Text = T("SAP00.Bom.Label.Stko", "STKO:");
        LblStas.Text = T("SAP00.Bom.Label.Stas", "STAS:");
        LblStpo.Text = T("SAP00.Bom.Label.Stpo", "STPO:");

        BtnSelectMast.Content = T("SAP00.Bom.Button.SelectMast", "Browse MAST");
        BtnSelectStko.Content = T("SAP00.Bom.Button.SelectStko", "Browse STKO");
        BtnSelectStas.Content = T("SAP00.Bom.Button.SelectStas", "Browse STAS");
        BtnSelectStpo.Content = T("SAP00.Bom.Button.SelectStpo", "Browse STPO");
        BtnImport.Content = T("SAP00.Bom.Button.Import", "Import BOMs");
        BtnLoadCache.Content = T("SAP00.Bom.Button.LoadCache", "Load latest cache");

        ExpColumnMapping.Header = T("SAP00.Bom.RequiredColumns", "Required export columns for MAST / STKO / STAS / STPO");

        ColMappingTable.Header = T("SAP00.Mapping.Column.Table", "Table");
        ColMappingColumn.Header = T("SAP00.Mapping.Column.Column", "Column");
        ColMappingRequired.Header = T("SAP00.Mapping.Column.Required", "Required");
        ColMappingSapMeaning.Header = T("SAP00.Mapping.Column.SapMeaning", "SAP meaning");
        ColMappingDmsMeaning.Header = T("SAP00.Mapping.Column.DmsMeaning", "DMS meaning");

        ColMaterial.Header = T("SAP00.Bom.Column.Material", "Material");
        ColPlant.Header = T("SAP00.Bom.Column.Plant", "Plant");
        ColMeaning.Header = T("SAP00.Bom.Column.Semantics", "Meaning");
        ColUsage.Header = T("SAP00.Bom.Column.Usage", "Usage");
        ColBom.Header = T("SAP00.Bom.Column.Bom", "BOM");
        ColAlternative.Header = T("SAP00.Bom.Column.Alternative", "Alt.");
        ColBaseQuantity.Header = T("SAP00.Bom.Column.BaseQuantity", "Base quantity");
        ColBaseUnit.Header = T("SAP00.Bom.Column.Uom", "UoM");
        ColItemCount.Header = T("SAP00.Bom.Column.ItemCount", "Items");
    }

    private void SetReadyMessage()
    {
        TxtResult.Text = T(
            "SAP00.Bom.Ready",
            "Ready to import BOMs.\n\nSelect SAP exports MAST, STKO, STAS and STPO.");
    }

    private void BtnSelectMast_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtMastFile, T("SAP00.Bom.Dialog.SelectMast", "Select MAST export"));
    }

    private void BtnSelectStko_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtStkoFile, T("SAP00.Bom.Dialog.SelectStko", "Select STKO export"));
    }

    private void BtnSelectStas_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtStasFile, T("SAP00.Bom.Dialog.SelectStas", "Select STAS export"));
    }

    private void BtnSelectStpo_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtStpoFile, T("SAP00.Bom.Dialog.SelectStpo", "Select STPO export"));
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }

        try
        {
            _logAction?.Invoke("ImportBomsStarted", $"Mast={TxtMastFile.Text.Trim()}; Stko={TxtStkoFile.Text.Trim()}; Stas={TxtStasFile.Text.Trim()}; Stpo={TxtStpoFile.Text.Trim()}; Cache={_cachePath}");

            var repository = new JsonSapBomRepository(_cachePath);
            var service = new SapBomExcelImportService(repository);

            var result = service.Import(
                TxtMastFile.Text.Trim(),
                TxtStkoFile.Text.Trim(),
                TxtStasFile.Text.Trim(),
                TxtStpoFile.Text.Trim());

            TxtResult.Text =
                TF("SAP00.Bom.Import.Completed", "Import completed.\n\nMAST rows: {0}\nSTKO rows: {1}\nSTPO rows: {2}\nImported BOMs: {3}\nImported items: {4}\nErrors: {5}",
                    result.MastRows,
                    result.StkoRows,
                    result.StpoRows,
                    result.ImportedBomCount,
                    result.ImportedItemCount,
                    result.ErrorRows) +
                "\n\n" +
                string.Join("\n", result.Messages);

            _logAction?.Invoke("ImportBomsCompleted", $"ImportedBoms={result.ImportedBomCount}; ImportedItems={result.ImportedItemCount}; Errors={result.ErrorRows}; Cache={_cachePath}");

            LoadCachePreview(showEmptyMessage: false);
        }
        catch (Exception ex)
        {
            _logAction?.Invoke("ImportBomsFailed", ex.Message);
            TxtResult.Text = TF("SAP00.Bom.Import.Failed", "BOM import failed.\n\n{0}", ex.Message);
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
                .Take(PreviewRowLimit)
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
                TxtResult.Text = boms.Count == 0
                    ? TF("SAP00.Bom.Cache.Empty", "No BOM cache has been loaded yet.\n\nExpected path:\n{0}", _cachePath)
                    : TF("SAP00.Bom.Cache.Loaded", "Loaded BOM cache: {0} records.\nThe grid shows only a preview of the first {1} records.\n\n{2}", boms.Count, rows.Count, _cachePath);
            }

            _logAction?.Invoke("LoadBomCachePreview", $"Count={boms.Count}; Preview={rows.Count}; Cache={_cachePath}");
        }
        catch (Exception ex)
        {
            _logAction?.Invoke("LoadBomCachePreviewFailed", ex.Message);
            TxtResult.Text = TF("SAP00.Bom.Cache.LoadFailed", "Failed to load BOM cache.\n\n{0}", ex.Message);
        }
    }

    private bool ValidateInputFiles()
    {
        if (!ValidateFile(TxtMastFile.Text.Trim(), "SAP00.Bom.Validation.Mast", "Select a valid MAST Excel export.")) return false;
        if (!ValidateFile(TxtStkoFile.Text.Trim(), "SAP00.Bom.Validation.Stko", "Select a valid STKO Excel export.")) return false;
        if (!ValidateFile(TxtStasFile.Text.Trim(), "SAP00.Bom.Validation.Stas", "Select a valid STAS Excel export.")) return false;
        if (!ValidateFile(TxtStpoFile.Text.Trim(), "SAP00.Bom.Validation.Stpo", "Select a valid STPO Excel export.")) return false;

        return true;
    }

    private bool ValidateFile(string filePath, string key, string fallback)
    {
        if (File.Exists(filePath))
        {
            return true;
        }

        TxtResult.Text = T(key, fallback);
        return false;
    }

    private void SelectExcelFileInto(TextBox target, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = T("SAP00.ExcelFilter", "Excel files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All files (*.*)|*.*")
        };

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return IsMissing(value, key) ? fallback : value!;
    }

    private string TF(string key, string fallback, params object[] args)
    {
        var pattern = _translateFormat?.Invoke(key, args);
        if (!string.IsNullOrWhiteSpace(pattern) && !IsMissing(pattern, key))
        {
            return pattern;
        }

        pattern = T(key, fallback);

        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
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
