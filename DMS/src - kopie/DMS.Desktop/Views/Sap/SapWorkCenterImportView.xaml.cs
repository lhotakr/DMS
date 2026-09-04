using DMS.Desktop.Configuration;
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
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly Action<string, string>? _logAction;

    public SapWorkCenterImportView()
        : this(new SapStoragePaths(DmsStoragePathPolicy.GetEnvironmentRoot("DEV")))
    {
    }

    public SapWorkCenterImportView(
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
        _cachePath = Path.Combine(storagePaths.SapMirrorDirectory, "sap-work-centers.json");

        DgvColumnMapping.ItemsSource = SapWorkCenterExcelColumnDefinitions.All;

        ApplyLocalization();
        SetReadyMessage();
        LoadCachePreview(showEmptyMessage: false);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAP00.WorkCenter.Title", "Work center import");
        TxtSubtitle.Text = T("SAP00.WorkCenter.Summary", "Import SE16N exports of CRHD and CRTX into the local DMS cache.");

        LblCrhd.Text = T("SAP00.WorkCenter.Label.Crhd", "CRHD:");
        LblCrtx.Text = T("SAP00.WorkCenter.Label.Crtx", "CRTX:");

        BtnSelectCrhd.Content = T("SAP00.WorkCenter.Button.SelectCrhd", "Browse CRHD");
        BtnSelectCrtx.Content = T("SAP00.WorkCenter.Button.SelectCrtx", "Browse CRTX");
        BtnImport.Content = T("SAP00.WorkCenter.Button.Import", "Start import");
        BtnLoadCache.Content = T("SAP00.WorkCenter.Button.LoadCache", "Load cache");

        ExpColumnMapping.Header = T("SAP00.WorkCenter.RequiredColumns", "Required export columns for CRHD / CRTX");

        ColMappingTable.Header = T("SAP00.Mapping.Column.Table", "Table");
        ColMappingColumn.Header = T("SAP00.Mapping.Column.Column", "Column");
        ColMappingRequired.Header = T("SAP00.Mapping.Column.Required", "Required");
        ColMappingSapMeaning.Header = T("SAP00.Mapping.Column.SapMeaning", "SAP meaning");
        ColMappingDmsMeaning.Header = T("SAP00.Mapping.Column.DmsMeaning", "DMS meaning");

        ColObjectId.Header = T("SAP00.WorkCenter.Column.ObjectId", "Object ID");
        ColWorkCenter.Header = T("SAP00.WorkCenter.Column.WorkCenter", "Work center");
        ColPlant.Header = T("SAP00.WorkCenter.Column.Plant", "Plant");
        ColWorkCenterText.Header = T("SAP00.WorkCenter.Column.Text", "Work center text");
        ColTextCount.Header = T("SAP00.WorkCenter.Column.TextCount", "Texts");
    }

    private void SetReadyMessage()
    {
        TxtResult.Text = T(
            "SAP00.WorkCenter.Ready",
            "Ready to import work centers.\n\nSelect SAP exports CRHD and CRTX.");
    }

    private void BtnSelectCrhd_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtCrhdFile, T("SAP00.WorkCenter.Dialog.SelectCrhd", "Select CRHD export"));
    }

    private void BtnSelectCrtx_Click(object sender, RoutedEventArgs e)
    {
        SelectExcelFileInto(TxtCrtxFile, T("SAP00.WorkCenter.Dialog.SelectCrtx", "Select CRTX export"));
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputFiles())
        {
            return;
        }

        try
        {
            _logAction?.Invoke("ImportWorkCentersStarted", $"Crhd={TxtCrhdFile.Text.Trim()}; Crtx={TxtCrtxFile.Text.Trim()}; Cache={_cachePath}");

            var repository = new JsonSapWorkCenterRepository(_cachePath);
            var service = new SapWorkCenterExcelImportService(repository);

            var result = service.Import(
                TxtCrhdFile.Text.Trim(),
                TxtCrtxFile.Text.Trim());

            DgvWorkCenters.ItemsSource = null;

            TxtResult.Text =
                TF("SAP00.WorkCenter.Import.Completed", "Work center import completed.\n\nCRHD rows: {0}\nCRTX rows: {1}\nImported work centers: {2}\nImported texts: {3}\nErrors: {4}",
                    result.CrhdRows,
                    result.CrtxRows,
                    result.ImportedWorkCenterCount,
                    result.ImportedTextCount,
                    result.ErrorRows) +
                "\n\n" +
                string.Join("\n", result.Messages) +
                "\n\n" +
                TF("SAP00.WorkCenter.Import.OutputFile", "Output file:\n{0}\n\nThe preview was not loaded automatically because of the data size. Use Load cache.", _cachePath);

            _logAction?.Invoke("ImportWorkCentersCompleted", $"ImportedWorkCenters={result.ImportedWorkCenterCount}; ImportedTexts={result.ImportedTextCount}; Errors={result.ErrorRows}; Cache={_cachePath}");
        }
        catch (Exception ex)
        {
            _logAction?.Invoke("ImportWorkCentersFailed", ex.Message);
            TxtResult.Text = TF("SAP00.WorkCenter.Import.Failed", "Work center import failed.\n\n{0}", ex.Message);
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
                TxtResult.Text = workCenters.Count == 0
                    ? TF("SAP00.WorkCenter.Cache.Empty", "No work center cache has been loaded yet.\n\nExpected path:\n{0}", _cachePath)
                    : TF("SAP00.WorkCenter.Cache.Loaded", "Loaded work center cache: {0} records.\nThe grid shows only a preview of the first {1} records.\n\n{2}", workCenters.Count, rows.Count, _cachePath);
            }

            _logAction?.Invoke("LoadWorkCenterCachePreview", $"Count={workCenters.Count}; Preview={rows.Count}; Cache={_cachePath}");
        }
        catch (Exception ex)
        {
            _logAction?.Invoke("LoadWorkCenterCachePreviewFailed", ex.Message);
            TxtResult.Text = TF("SAP00.WorkCenter.Cache.LoadFailed", "Failed to load work center cache.\n\n{0}", ex.Message);
        }
    }

    private bool ValidateInputFiles()
    {
        if (!ValidateFile(TxtCrhdFile.Text.Trim(), "SAP00.WorkCenter.Validation.Crhd", "Select a valid CRHD Excel export.")) return false;
        if (!ValidateFile(TxtCrtxFile.Text.Trim(), "SAP00.WorkCenter.Validation.Crtx", "Select a valid CRTX Excel export.")) return false;

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

    private sealed class SapWorkCenterGridRow
    {
        public string ObjectId { get; init; } = string.Empty;
        public string WorkCenter { get; init; } = string.Empty;
        public string Plant { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public int TextCount { get; init; }
    }
}
