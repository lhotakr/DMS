using ClosedXML.Excel;
using DMS.Core.Quality;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views.Quality;

public partial class QualityArticleCreateView : UserControl
{
    private readonly QualityArticleCreateService _service;
    private readonly JsonQualityRepository _repository;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private IReadOnlyList<QualityCustomer> _customers = Array.Empty<QualityCustomer>();
    private IReadOnlyList<QualityLookupItem> _colorTypes = Array.Empty<QualityLookupItem>();
    private IReadOnlyList<QualityLookupItem> _glassTreatments = Array.Empty<QualityLookupItem>();
    private IReadOnlyList<QualityLookupItem> _qualityClasses = Array.Empty<QualityLookupItem>();

    private QualityArticleCreateModel? _model;
    private bool _isLoading;
    private bool _hasLoadedSap;

    public event Action<string>? TransactionRequested;

    public QualityArticleCreateView(string query)
        : this(
            query,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            null,
            null,
            null,
            null)
    {
    }

    public QualityArticleCreateView(
        string query,
        string dmsRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var qualityPaths = new QualityStoragePaths(rootPath);
        qualityPaths.EnsureDirectories();

        _repository = new JsonQualityRepository(qualityPaths);

        var sapStoragePaths = new SapStoragePaths(rootPath);
        sapStoragePaths.EnsureDirectories();

        var sapMaterials =
            new JsonSapMaterialRepository(
                    sapStoragePaths.SapMaterialsFilePath)
                .LoadAll();

        var decorationRulesPath = Path.Combine(
            rootPath,
            "Config",
            "sap-decoration-rules.json");

        var decorationRules =
            new SapDecorationRulesLoader()
                .LoadFromJson(decorationRulesPath);

        var decorationRuleService =
            new SapDecorationRuleService(decorationRules);

        _service = new QualityArticleCreateService(
            _repository,
            sapMaterials,
            decorationRuleService);

        ApplyLocalization();
        LoadLookupData();

        _logger?.AdminAction(
            "QA01",
            "OpenQualityArticleCreate",
            _currentUserName,
            $"Root={rootPath}; Query={query}");

        if (!string.IsNullOrWhiteSpace(query))
        {
            TxtSapInput.Text = query.Trim();
            LoadSap();
        }
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("QA01.Title");
        TxtSubtitle.Text = T("QA01.Subtitle");

        TxtSectionSapMaterial.Text = T("QA01.Section.SapMaterial");
        LblSapNumber.Text = T("QA01.Field.SapNumber");
        BtnLoadSap.Content = T("QA01.Action.LoadSap");

        TxtSectionSapBase.Text = T("QA01.Section.SapBase");
        LblSapId.Text = T("QA01.Field.SapId");
        LblOldNumber.Text = T("QA01.Field.OldNumber");
        LblSapTitle.Text = T("QA01.Field.SapTitle");

        TxtSectionArticleInfo.Text = T("QA01.Section.ArticleInfo");
        LblImportantInfo.Text = T("QA01.Field.ImportantInfo");
        LblArticleNotes.Text = T("QA01.Field.ArticleNotes");

        TxtSectionFirstPrintVersion.Text = T("QA01.Section.FirstPrintVersion");
        LblPrintVersionNumber.Text = T("QA01.Field.PrintVersionNumber");
        LblPrintVersionTitle.Text = T("QA01.Field.PrintVersionTitle");
        LblCustomer.Text = T("QA01.Field.Customer");
        LblDecoration.Text = T("QA01.Field.Decoration");
        TxtDecoration.ToolTip = T("QA01.Tooltip.DecorationFromSap");
        LblColorType.Text = T("QA01.Field.ColorType");
        TxtColorType.ToolTip = T("QA01.Tooltip.MultipleValues");
        BtnClearColorTypes.Content = T("QA01.Action.Clear");
        BtnApplyColorTypes.Content = T("QA01.Action.Apply");
        LblGlassTreatment.Text = T("QA01.Field.GlassTreatment");
        LblQualityClass.Text = T("QA01.Field.QualityClass");
        LblHdNumber.Text = T("QA01.Field.HdNumber");
        LblSampleLocation.Text = T("QA01.Field.SampleLocation");
        LblBoardLocation.Text = T("QA01.Field.BoardLocation");
        LblGaugeLocation.Text = T("QA01.Field.GaugeLocation");
        ChkHasGauge.Content = T("QA01.Flag.HasGauge");
        ChkComplaint.Content = T("QA01.Flag.Complaint");
        ChkSamplesOnCamera.Content = T("QA01.Flag.SamplesOnCamera");
        LblPrintVersionNotes.Text = T("QA01.Field.Notes");

        BtnCreate.Content = T("QA01.Action.Create");
        BtnClear.Content = T("QA01.Action.Clear");
    }

    // ============================================================
    // SAP LOAD
    // ============================================================

    private void BtnLoadSap_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadSap();
    }

    private void TxtSapInput_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        LoadSap();
    }

    private void LoadSap()
    {
        ClearWarning();

        var sapInput =
            TxtSapInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(sapInput))
        {
            ShowWarning(T("QA01.Warning.EnterSapNumber"));
            return;
        }

        _model =
            _service.TryPrepareFromSap(sapInput);

        if (_model is null)
        {
            ShowWarning(TF("QA01.Warning.SapNotFound", sapInput));

            _logger?.AdminAction(
                "QA01",
                "SapMaterialNotFound",
                _currentUserName,
                $"SapMaterial={sapInput}");

            PrintVersionForm.IsEnabled = false;
            BtnCreate.IsEnabled = false;
            _hasLoadedSap = false;
            return;
        }

        if (_service.ExistsSapMaterialQualityData(
                _model.SapMaterialNumber))
        {
            ShowWarning(TF("QA01.Warning.QualityExists", _model.SapMaterialNumber));

            BtnCreate.IsEnabled = false;
            PrintVersionForm.IsEnabled = false;

            FillSapPreview(_model);
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                _model.FullPrintVersionNumber) &&
            _service.ExistsPrintVersion(
                _model.FullPrintVersionNumber))
        {
            ShowWarning(TF("QA01.Warning.PrintVersionExists", _model.FullPrintVersionNumber));

            BtnCreate.IsEnabled = false;
            PrintVersionForm.IsEnabled = false;

            FillSapPreview(_model);
            return;
        }

        _hasLoadedSap = true;

        FillForm(_model);

        PrintVersionForm.IsEnabled = true;
        BtnCreate.IsEnabled = true;

        _logger?.AdminAction(
            "QA01",
            "SapMaterialLoaded",
            _currentUserName,
            $"SapMaterial={_model.SapMaterialNumber}; PrintVersion={_model.FullPrintVersionNumber}");
    }

    private void FillSapPreview(
        QualityArticleCreateModel model)
    {
        _isLoading = true;

        try
        {
            TxtSapMaterial.Text = model.SapMaterialNumber;
            TxtSapTitle.Text = model.SapTitle;
            TxtOldMaterialNumber.Text = model.OldMaterialNumber;
            TxtDecoration.Text = model.DecorationCode;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void FillForm(
        QualityArticleCreateModel model)
    {
        _isLoading = true;

        try
        {
            TxtSapMaterial.Text = model.SapMaterialNumber;
            TxtSapTitle.Text = model.SapTitle;
            TxtOldMaterialNumber.Text = model.OldMaterialNumber;

            TxtPrintVersion.Text = model.FullPrintVersionNumber;
            TxtPrintVersionTitle.Text = model.PrintVersionTitle;
            TxtDecoration.Text = model.DecorationCode;

            TxtCustomer.Text = model.Customer;
            TxtColorType.Text = model.ColorType;
            TxtGlassTreatment.Text = model.GlassTreatment;
            TxtQualityClass.Text = model.QualityClass;

            TxtHdNumber.Text = model.HdNumber;
            TxtSampleLocation.Text = model.SampleLocation;
            TxtBoardLocation.Text = model.BoardLocation;
            TxtGaugeLocation.Text = model.GaugeLocation;

            ChkHasGauge.IsChecked = model.HasGauge;
            ChkComplaint.IsChecked = model.HasComplaint;
            ChkSamplesOnCamera.IsChecked = model.SamplesOnCamera;

            TxtImportantInfo.Text = model.ImportantInfo;
            TxtArticleNotes.Text = model.ArticleNotes;
            TxtPrintVersionNotes.Text = model.PrintVersionNotes;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void WriteFormToModel()
    {
        _model ??= new QualityArticleCreateModel();

        _model.SapMaterialNumber =
            TxtSapMaterial.Text.Trim();

        _model.SapTitle =
            TxtSapTitle.Text.Trim();

        _model.OldMaterialNumber =
            TxtOldMaterialNumber.Text.Trim();

        _model.FullPrintVersionNumber =
            TxtPrintVersion.Text.Trim();

        _model.PrintVersionTitle =
            TxtPrintVersionTitle.Text.Trim();

        _model.DecorationCode =
            TxtDecoration.Text.Trim();

        _model.Customer =
            TxtCustomer.Text.Trim();

        _model.ColorType =
            TxtColorType.Text.Trim();

        _model.GlassTreatment =
            TxtGlassTreatment.Text.Trim();

        _model.QualityClass =
            TxtQualityClass.Text.Trim();

        _model.HdNumber =
            TxtHdNumber.Text.Trim();

        _model.SampleLocation =
            TxtSampleLocation.Text.Trim();

        _model.BoardLocation =
            TxtBoardLocation.Text.Trim();

        _model.GaugeLocation =
            TxtGaugeLocation.Text.Trim();

        _model.HasGauge =
            ChkHasGauge.IsChecked == true;

        _model.HasComplaint =
            ChkComplaint.IsChecked == true;

        _model.SamplesOnCamera =
            ChkSamplesOnCamera.IsChecked == true;

        _model.ImportantInfo =
            TxtImportantInfo.Text;

        _model.ArticleNotes =
            TxtArticleNotes.Text;

        _model.PrintVersionNotes =
            TxtPrintVersionNotes.Text;
    }

    // ============================================================
    // CREATE
    // ============================================================

    private void BtnCreate_Click(
        object sender,
        RoutedEventArgs e)
    {
        ClearWarning();

        if (!_hasLoadedSap || _model is null)
        {
            ShowWarning(T("QA01.Warning.LoadSapFirst"));

            return;
        }

        WriteFormToModel();

        var result =
            _service.Create(_model);

        if (!result.Success)
        {
            ShowWarning(result.Message);
            return;
        }

        LogCreatedQualityData(result.CreatedPrintVersionNumber);

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("QA01.Dialog.Created.Title"),
            TF("QA01.Dialog.Created.Message",
                _model.SapMaterialNumber,
                result.CreatedPrintVersionNumber));

        TransactionRequested?.Invoke(
            $"QA03 {result.CreatedPrintVersionNumber}");
    }

    private void LogCreatedQualityData(string? createdPrintVersionNumber)
    {
        if (_model is null)
        {
            return;
        }

        _logger?.AuditCreated(
            "QA01",
            "QualityArticle",
            _model.SapMaterialNumber,
            _currentUserName,
            $"SapTitle={_model.SapTitle}; OldMaterialNumber={_model.OldMaterialNumber}; Customer={_model.Customer}; ImportantInfo={_model.ImportantInfo}; IsActive=True");

        _logger?.AuditCreated(
            "QA01",
            "QualityPrintVersion",
            string.IsNullOrWhiteSpace(createdPrintVersionNumber)
                ? _model.FullPrintVersionNumber
                : createdPrintVersionNumber,
            _currentUserName,
            $"SapMaterial={_model.SapMaterialNumber}; Title={_model.PrintVersionTitle}; Decoration={_model.DecorationCode}; ColorType={_model.ColorType}; GlassTreatment={_model.GlassTreatment}; QualityClass={_model.QualityClass}; HdNumber={_model.HdNumber}; HasGauge={_model.HasGauge}; Complaint={_model.HasComplaint}; SamplesOnCamera={_model.SamplesOnCamera}");

        _logger?.AdminAction(
            "QA01",
            "CreateQualityData",
            _currentUserName,
            $"SapMaterial={_model.SapMaterialNumber}; PrintVersion={createdPrintVersionNumber}; Customer={_model.Customer}");
    }

    private void BtnClear_Click(
        object sender,
        RoutedEventArgs e)
    {
        ClearAll();
    }

    private void ClearAll()
    {
        _model = null;
        _hasLoadedSap = false;

        TxtSapInput.Clear();
        TxtSapMaterial.Clear();
        TxtSapTitle.Clear();
        TxtOldMaterialNumber.Clear();

        TxtImportantInfo.Clear();
        TxtArticleNotes.Clear();

        TxtPrintVersion.Clear();
        TxtPrintVersionTitle.Clear();
        TxtDecoration.Clear();
        TxtCustomer.Clear();
        TxtColorType.Clear();
        TxtGlassTreatment.Clear();
        TxtQualityClass.Clear();
        TxtHdNumber.Clear();
        TxtSampleLocation.Clear();
        TxtBoardLocation.Clear();
        TxtGaugeLocation.Clear();
        TxtPrintVersionNotes.Clear();

        ChkHasGauge.IsChecked = false;
        ChkComplaint.IsChecked = false;
        ChkSamplesOnCamera.IsChecked = false;

        PrintVersionForm.IsEnabled = false;
        BtnCreate.IsEnabled = false;

        ClearWarning();

        _logger?.AdminAction(
            "QA01",
            "ClearQualityArticleCreateForm",
            _currentUserName,
            string.Empty);
    }

    // ============================================================
    // LOOKUPS
    // ============================================================

    private void LoadLookupData()
    {
        _customers = _repository
            .LoadCustomers()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .ToList();

        _colorTypes = _repository
            .LoadColorTypes()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToList();

        _glassTreatments = _repository
            .LoadGlassTreatments()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToList();

        _qualityClasses = _repository
            .LoadQualityClasses()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToList();

        ListCustomers.ItemsSource = _customers;
        ListColorTypes.ItemsSource = _colorTypes;
        ListGlassTreatments.ItemsSource = _glassTreatments;
        ListQualityClasses.ItemsSource = _qualityClasses;
    }

    private void BtnOpenCustomerSelector_Click(
        object sender,
        RoutedEventArgs e)
    {
        PopupCustomers.IsOpen = true;
    }

    private void BtnOpenColorTypeSelector_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectColorTypesFromText(
            TxtColorType.Text);

        PopupColorTypes.IsOpen = true;
    }

    private void BtnOpenGlassTreatmentSelector_Click(
        object sender,
        RoutedEventArgs e)
    {
        PopupGlassTreatments.IsOpen = true;
    }

    private void BtnOpenQualityClassSelector_Click(
        object sender,
        RoutedEventArgs e)
    {
        PopupQualityClasses.IsOpen = true;
    }

    private void ListCustomers_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isLoading ||
            ListCustomers.SelectedItem is not QualityCustomer customer)
        {
            return;
        }

        TxtCustomer.Text = customer.Name;
        PopupCustomers.IsOpen = false;
    }

    private void BtnApplyColorTypes_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selectedNames = _colorTypes
            .Where(item =>
                ListColorTypes.SelectedItems.Contains(item))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .Select(item => item.Name)
            .ToList();

        TxtColorType.Text =
            string.Join(", ", selectedNames);

        PopupColorTypes.IsOpen = false;
    }

    private void BtnClearColorTypes_Click(
        object sender,
        RoutedEventArgs e)
    {
        ListColorTypes.SelectedItems.Clear();
        TxtColorType.Clear();
        PopupColorTypes.IsOpen = false;
    }

    private void SelectColorTypesFromText(
        string? currentText)
    {
        ListColorTypes.SelectedItems.Clear();

        var selectedNames = SplitLookupValues(currentText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _colorTypes)
        {
            if (selectedNames.Contains(item.Name) ||
                selectedNames.Contains(item.Code))
            {
                ListColorTypes.SelectedItems.Add(item);
            }
        }
    }

    private void ListGlassTreatments_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isLoading ||
            ListGlassTreatments.SelectedItem is not QualityLookupItem item)
        {
            return;
        }

        TxtGlassTreatment.Text = item.Name;
        PopupGlassTreatments.IsOpen = false;
    }

    private void ListQualityClasses_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isLoading ||
            ListQualityClasses.SelectedItem is not QualityLookupItem item)
        {
            return;
        }

        TxtQualityClass.Text = item.Name;
        PopupQualityClasses.IsOpen = false;
    }

    private static IEnumerable<string> SplitLookupValues(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Enumerable.Empty<string>();
        }

        return value
            .Split(
                new[] { ",", ";", "|", ";#" },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item =>
                !string.IsNullOrWhiteSpace(item));
    }

    // ============================================================
    // UI HELPERS
    // ============================================================

    private void EditableControl_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        ClearWarning();
    }

    private void EditableControl_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        ClearWarning();
    }

    private void ShowWarning(string message)
    {
        DuplicateWarningBorder.Visibility =
            Visibility.Visible;

        TxtDuplicateWarning.Text =
            message;
    }

    private void ClearWarning()
    {
        DuplicateWarningBorder.Visibility =
            Visibility.Collapsed;

        TxtDuplicateWarning.Text =
            string.Empty;
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var translated = _translateFormat(key, args);
            if (!IsMissing(translated, key))
            {
                return translated;
            }
        }

        var pattern = T(key);

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
}