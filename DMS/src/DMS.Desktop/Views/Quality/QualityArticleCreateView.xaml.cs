using DMS.Core.Quality;
using DMS.Core.Sap;
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

    private IReadOnlyList<QualityCustomer> _customers = Array.Empty<QualityCustomer>();
    private IReadOnlyList<QualityLookupItem> _colorTypes = Array.Empty<QualityLookupItem>();
    private IReadOnlyList<QualityLookupItem> _glassTreatments = Array.Empty<QualityLookupItem>();
    private IReadOnlyList<QualityLookupItem> _qualityClasses = Array.Empty<QualityLookupItem>();

    private QualityArticleCreateModel? _model;
    private bool _isLoading;
    private bool _hasLoadedSap;

    public event Action<string>? TransactionRequested;

    public QualityArticleCreateView(string query)
    {
        InitializeComponent();

        const string basePath = @"Z:\SAP\DMS-db\DEV";

        var qualityPaths = new QualityStoragePaths(basePath);
        qualityPaths.EnsureDirectories();

        _repository = new JsonQualityRepository(qualityPaths);

        var sapStoragePaths = new SapStoragePaths(basePath);
        sapStoragePaths.EnsureDirectories();

        var sapMaterials =
            new JsonSapMaterialRepository(
                    sapStoragePaths.SapMaterialsFilePath)
                .LoadAll();

        var decorationRulesPath = Path.Combine(
            basePath,
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

        LoadLookupData();

        if (!string.IsNullOrWhiteSpace(query))
        {
            TxtSapInput.Text = query.Trim();
            LoadSap();
        }
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
            ShowWarning("Zadej SAP číslo materiálu.");
            return;
        }

        _model =
            _service.TryPrepareFromSap(sapInput);

        if (_model is null)
        {
            ShowWarning(
                $"SAP materiál {sapInput} nebyl nalezen v lokální SAP cache.");

            PrintVersionForm.IsEnabled = false;
            BtnCreate.IsEnabled = false;
            _hasLoadedSap = false;
            return;
        }

        if (_service.ExistsSapMaterialQualityData(
                _model.SapMaterialNumber))
        {
            ShowWarning(
                $"Quality data pro SAP {_model.SapMaterialNumber} už existují.\n\n" +
                "Pro změnu použij QA02 nebo pro náhled QA03.");

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
            ShowWarning(
                $"Tisková verze {_model.FullPrintVersionNumber} už existuje.\n\n" +
                "Číslo tiskové verze musí být unikátní.");

            BtnCreate.IsEnabled = false;
            PrintVersionForm.IsEnabled = false;

            FillSapPreview(_model);
            return;
        }

        _hasLoadedSap = true;

        FillForm(_model);

        PrintVersionForm.IsEnabled = true;
        BtnCreate.IsEnabled = true;
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
            ShowWarning(
                "Nejdřív načti SAP materiál ze SAP cache.");

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

        MessageBox.Show(
            result.Message,
            "QA01 - založeno",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        TransactionRequested?.Invoke(
            $"QA03 {result.CreatedPrintVersionNumber}");
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
    }

    // ============================================================
    // LOOKUPY
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
}