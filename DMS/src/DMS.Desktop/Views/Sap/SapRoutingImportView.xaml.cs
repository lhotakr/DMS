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
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly Action<string, string>? _logAction;

    public SapRoutingImportView()
        : this(new SapStoragePaths(@"Z:\SAP\DMS-db\DEV"))
    {
    }

    public SapRoutingImportView(
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
        _cachePath = Path.Combine(storagePaths.SapMirrorDirectory, "sap-routings.json");

        DgvColumnMapping.ItemsSource = SapRoutingExcelColumnDefinitions.All;

        ApplyLocalization();
        SetReadyMessage();
        LoadCachePreview(showEmptyMessage: false);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAP00.Routing.Title", "Routing import");
        TxtSubtitle.Text = T("SAP00.Routing.Summary", "Import SE16N exports of MAPL, PLKO, PLAS and PLPO into the local DMS cache. Alternative routings are split by PLAS.");

        LblMapl.Text = T("SAP00.Routing.Label.Mapl", "MAPL:");
        LblPlko.Text = T("SAP00.Routing.Label.Plko", "PLKO:");
        LblPlpo.Text = T("SAP00.Routing.Label.Plpo", "PLPO:");
        LblPlas.Text = T("SAP00.Routing.Label.Plas", "PLAS:");

        BtnSelectMapl.Content = T("SAP00.Routing.Button.SelectMapl", "Browse MAPL");
        BtnSelectPlko.Content = T("SAP00.Routing.Button.SelectPlko", "Browse PLKO");
        BtnSelectPlpo.Content = T("SAP00.Routing.Button.SelectPlpo", "Browse PLPO");
        BtnSelectPlas.Content = T("SAP00.Routing.Button.SelectPlas", "Browse PLAS");
        BtnImport.Content = T("SAP00.Routing.Button.Import", "Start import");
        BtnLoadCache.Content = T("SAP00.Routing.Button.LoadCache", "Load cache");

        ExpColumnMapping.Header = T("SAP00.Routing.RequiredColumns", "Required export columns for MAPL / PLKO / PLAS / PLPO");

        ColMappingTable.Header = T("SAP00.Mapping.Column.Table", "Table");
        ColMappingColumn.Header = T("SAP00.Mapping.Column.Column", "Column");
        ColMappingRequired.Header = T("SAP00.Mapping.Column.Required", "Required");
        ColMappingSapMeaning.Header = T("SAP00.Mapping.Column.SapMeaning", "SAP meaning");
        ColMappingDmsMeaning.Header = T("SAP00.Mapping.Column.DmsMeaning", "DMS meaning");

        ColMaterial.Header = T("SAP00.Routing.Column.Material", "Material");
        ColPlant.Header = T("SAP00.Routing.Column.Plant", "Plant");
        ColMeaning.Header = T("SAP00.Routing.Column.Semantics", "Meaning");
        ColGroup.Header = T("SAP00.Routing.Column.Group", "Group");
        ColAlternative.Header = T("SAP00.Routing.Column.Alternative", "Alt.");
        ColDescription.Header = T("SAP00.Routing.Column.Description", "Description");
        ColOperationCount.Header = T("SAP00.Routing.Column.OperationCount", "Operations");
        ColWarningCount.Header = T("SAP00.Routing.Column.WarningCount", "Warnings");
        ColCriticalError.Header = T("SAP00.Routing.Column.CriticalError", "Critical error");
    }

    private void SetReadyMessage()
    {
        TxtResult.Text = T(
            "SAP00.Routing.Ready",
            "Ready to import routings.\n\nSelect SAP exports MAPL, PLKO, PLAS and PLPO.");
    }

    private void BtnSelectMapl_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtMaplFile, T("SAP00.Routing.Dialog.SelectMapl", "Select MAPL export"));
    }

    private void BtnSelectPlko_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtPlkoFile, T("SAP00.Routing.Dialog.SelectPlko", "Select PLKO export"));
    }

    private void BtnSelectPlpo_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtPlpoFile, T("SAP00.Routing.Dialog.SelectPlpo", "Select PLPO export"));
    }

    private void BtnSelectPlas_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtPlasFile, T("SAP00.Routing.Dialog.SelectPlas", "Select PLAS export"));
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }

        try
        {
            _logAction?.Invoke("ImportRoutingsStarted", $"Mapl={TxtMaplFile.Text.Trim()}; Plko={TxtPlkoFile.Text.Trim()}; Plas={TxtPlasFile.Text.Trim()}; Plpo={TxtPlpoFile.Text.Trim()}; Cache={_cachePath}");

            var repository = new JsonSapRoutingRepository(_cachePath);
            var service = new SapRoutingExcelImportService(repository);

            var result = service.Import(
                TxtMaplFile.Text.Trim(),
                TxtPlkoFile.Text.Trim(),
                TxtPlasFile.Text.Trim(),
                TxtPlpoFile.Text.Trim());

            DgvRoutings.ItemsSource = null;

            TxtResult.Text =
                TF("SAP00.Routing.Import.Completed", "Routing import completed.\n\nMAPL rows: {0}\nPLKO rows: {1}\nPLPO rows: {2}\nImported routings: {3}\nImported operations: {4}\nSkipped alternatives PLNAL != 1: {5}\nWarnings: {6}\nErrors: {7}",
                    result.MaplRows,
                    result.PlkoRows,
                    result.PlpoRows,
                    result.ImportedRoutingCount,
                    result.ImportedOperationCount,
                    result.SkippedAlternativeCount,
                    result.WarningCount,
                    result.ErrorRows) +
                "\n\n" +
                string.Join("\n", result.Messages) +
                "\n\n" +
                TF("SAP00.Routing.Import.OutputFile", "Output file:\n{0}\n\nThe preview was not loaded automatically because of the data size. Use Load cache.", _cachePath);

            _logAction?.Invoke("ImportRoutingsCompleted", $"ImportedRoutings={result.ImportedRoutingCount}; ImportedOperations={result.ImportedOperationCount}; Warnings={result.WarningCount}; Errors={result.ErrorRows}; Cache={_cachePath}");
        }
        catch (Exception ex)
        {
            _logAction?.Invoke("ImportRoutingsFailed", ex.Message);
            TxtResult.Text = TF("SAP00.Routing.Import.Failed", "Routing import failed.\n\n{0}", ex.Message);
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
                TxtResult.Text = routings.Count == 0
                    ? TF("SAP00.Routing.Cache.Empty", "No routing cache has been loaded yet.\n\nExpected path:\n{0}", _cachePath)
                    : TF("SAP00.Routing.Cache.Loaded", "Loaded routing cache: {0} records.\nThe grid shows only a preview of the first {1} records.\n\n{2}", routings.Count, rows.Count, _cachePath);
            }

            _logAction?.Invoke("LoadRoutingCachePreview", $"Count={routings.Count}; Preview={rows.Count}; Cache={_cachePath}");
        }
        catch (Exception ex)
        {
            _logAction?.Invoke("LoadRoutingCachePreviewFailed", ex.Message);
            TxtResult.Text = TF("SAP00.Routing.Cache.LoadFailed", "Failed to load routing cache.\n\n{0}", ex.Message);
        }
    }

    private bool ValidateInputFiles()
    {
        if (!ValidateFile(TxtMaplFile.Text.Trim(), "SAP00.Routing.Validation.Mapl", "Select a valid MAPL Excel export.")) return false;
        if (!ValidateFile(TxtPlkoFile.Text.Trim(), "SAP00.Routing.Validation.Plko", "Select a valid PLKO Excel export.")) return false;
        if (!ValidateFile(TxtPlpoFile.Text.Trim(), "SAP00.Routing.Validation.Plpo", "Select a valid PLPO Excel export.")) return false;
        if (!ValidateFile(TxtPlasFile.Text.Trim(), "SAP00.Routing.Validation.Plas", "Select a valid PLAS Excel export.")) return false;

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
