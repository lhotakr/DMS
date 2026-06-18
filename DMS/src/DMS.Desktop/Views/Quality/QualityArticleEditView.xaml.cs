using DMS.Core.Quality;
using DMS.Desktop.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views.Quality;

public partial class QualityArticleEditView :
    UserControl,
    IUnsavedChangesGuard
{
    private IReadOnlyList<QualityCustomer> _customers = Array.Empty<QualityCustomer>();
    private IReadOnlyList<QualityLookupItem> _colorTypes = Array.Empty<QualityLookupItem>();
    private IReadOnlyList<QualityLookupItem> _glassTreatments = Array.Empty<QualityLookupItem>();
    private IReadOnlyList<QualityLookupItem> _qualityClasses = Array.Empty<QualityLookupItem>();

    private readonly QualityArticleEditService _service;
    private readonly string _query;
    private readonly JsonQualityRepository _repository;

    private QualityArticleEditModel? _model;
    private QualityPrintVersionEditModel? _selectedPrintVersion;

    private bool _identityWarningShown;
    private bool _isLoading;
    private bool _hasUnsavedChanges;
    private bool _allowNavigationAfterSave;

    private readonly HashSet<Control> _changedControls = new();
    private readonly List<QualityTaskEditModel> _subscribedTasks = new();

    public event Action<string>? TransactionRequested;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public QualityArticleEditView(string query)
    {
        InitializeComponent();

        _query = query;

        const string basePath = @"Z:\SAP\DMS-db\DEV";

        var paths = new QualityStoragePaths(basePath);

        _repository = new JsonQualityRepository(paths);
        _service = new QualityArticleEditService(_repository);

        LoadLookupData();
        LoadData();
    }

    // ============================================================
    // NAČTENÍ
    // ============================================================

    private void LoadData()
    {
        _isLoading = true;

        try
        {
            _model = _service.Load(_query);

            if (_model is null)
            {
                MessageBox.Show(
                    $"Pro dotaz {_query} nebyla nalezena quality data.",
                    "QA02",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                IsEnabled = false;
                return;
            }

            IsEnabled = true;

            TxtImportantInfo.Text = _model.ImportantInfo;
            TxtArticleNotes.Text = _model.ArticleNotes;

            ListPrintVersions.ItemsSource = _model.PrintVersions;

            _selectedPrintVersion = SelectInitialPrintVersion(_model);

            if (_selectedPrintVersion is not null)
            {
                ListPrintVersions.SelectedItem = _selectedPrintVersion;
            }

            LoadSelectedPrintVersion();

            ClearDirtyState();
            _identityWarningShown = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private QualityPrintVersionEditModel? SelectInitialPrintVersion(
        QualityArticleEditModel model)
    {
        var exactMatch = model.PrintVersions.FirstOrDefault(item =>
            string.Equals(
                item.FullPrintVersionNumber,
                _query,
                StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var sapMatch = model.PrintVersions.FirstOrDefault(item =>
            string.Equals(
                NormalizeSapNumber(item.SapMaterialNumber),
                NormalizeSapNumber(_query),
                StringComparison.OrdinalIgnoreCase));

        return sapMatch ?? model.PrintVersions.FirstOrDefault();
    }

    private void LoadSelectedPrintVersion()
    {
        _isLoading = true;

        try
        {
            TxtSelectedPrintVersion.Text =
                _selectedPrintVersion?.ToString() ?? string.Empty;

            PrintVersionForm.IsEnabled =
                _selectedPrintVersion is not null;

            GridTasks.IsEnabled =
                _selectedPrintVersion is not null;

            if (_selectedPrintVersion is null)
            {
                ClearPrintVersionForm();
                return;
            }

            TxtPrintVersion.Text =
                _selectedPrintVersion.FullPrintVersionNumber;

            TxtSapMaterial.Text =
                _selectedPrintVersion.SapMaterialNumber;

            TxtTitle.Text =
                _selectedPrintVersion.Title;

            TxtCustomer.Text =
                _selectedPrintVersion.Customer;

            TxtDecoration.Text =
                _selectedPrintVersion.DecorationCode;

            TxtColorType.Text =
                _selectedPrintVersion.ColorType;

            TxtGlassTreatment.Text =
                _selectedPrintVersion.GlassTreatment;

            TxtQualityClass.Text =
                _selectedPrintVersion.QualityClass;

            TxtHdNumber.Text =
                _selectedPrintVersion.HdNumber;

            TxtSampleLocation.Text =
                _selectedPrintVersion.SampleLocation;

            TxtBoardLocation.Text =
                _selectedPrintVersion.BoardLocation;

            TxtGaugeLocation.Text =
                _selectedPrintVersion.GaugeLocation;

            ChkHasGauge.IsChecked =
                _selectedPrintVersion.HasGauge;

            ChkComplaint.IsChecked =
                _selectedPrintVersion.HasComplaint;

            ChkSamplesOnCamera.IsChecked =
                _selectedPrintVersion.SamplesOnCamera;

            TxtPrintVersionNotes.Text =
                _selectedPrintVersion.Notes;

            UnsubscribeTaskChanges();

            GridTasks.ItemsSource =
                _selectedPrintVersion.Tasks;

            SubscribeTaskChanges();
            UpdateTaskStatus();

            TxtPrintVersion.IsReadOnly = true;
            TxtSapMaterial.IsReadOnly = true;
            TxtDecoration.IsReadOnly = true;

            ClearDirtyState();
            _identityWarningShown = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ClearPrintVersionForm()
    {
        UnsubscribeTaskChanges();

        TxtSelectedPrintVersion.Clear();
        TxtPrintVersion.Clear();
        TxtSapMaterial.Clear();
        TxtTitle.Clear();
        TxtCustomer.Clear();
        TxtDecoration.Clear();
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

        GridTasks.ItemsSource = null;

        TxtTaskStatus.Text = "Bez vybrané tiskové verze";
    }

    // ============================================================
    // VÝBĚR TISKOVÉ VERZE
    // ============================================================

    private void BtnOpenPrintVersionSelector_Click(
        object sender,
        RoutedEventArgs e)
    {
        PopupPrintVersions.IsOpen = true;
    }

    private void ListPrintVersions_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isLoading ||
            ListPrintVersions.SelectedItem is not QualityPrintVersionEditModel selected)
        {
            return;
        }

        TrySwitchPrintVersion(selected);
    }

    private void ListPrintVersions_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        PopupPrintVersions.IsOpen = false;
    }

    private void TrySwitchPrintVersion(
        QualityPrintVersionEditModel selected)
    {
        if (ReferenceEquals(selected, _selectedPrintVersion))
        {
            PopupPrintVersions.IsOpen = false;
            return;
        }

        if (_hasUnsavedChanges)
        {
            var result = DmsConfirmDialog.Show(
                Window.GetWindow(this),
                "Neuložené změny",
                "V QA02 jsou neuložené změny.\n\n" +
                "Chceš je před odchodem uložit?",
                showCancel: true);

            if (result == MessageBoxResult.Cancel)
            {
                RestoreSelectedListItem();
                return;
            }

            if (result == MessageBoxResult.Yes && !TrySave())
            {
                RestoreSelectedListItem();
                return;
            }

            if (result == MessageBoxResult.No)
            {
                ClearDirtyState();
            }
        }

        _selectedPrintVersion = selected;

        PopupPrintVersions.IsOpen = false;

        LoadSelectedPrintVersion();
    }

    private void RestoreSelectedListItem()
    {
        _isLoading = true;

        try
        {
            ListPrintVersions.SelectedItem =
                _selectedPrintVersion;
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ============================================================
    // PŘENOS FORMULÁŘE DO MODELU
    // ============================================================

    private void WriteFormToSelectedPrintVersion()
    {
        if (_selectedPrintVersion is null)
        {
            return;
        }

        GridTasks.CommitEdit(
            DataGridEditingUnit.Cell,
            true);

        GridTasks.CommitEdit(
            DataGridEditingUnit.Row,
            true);

        _selectedPrintVersion.FullPrintVersionNumber =
            TxtPrintVersion.Text.Trim();

        _selectedPrintVersion.SapMaterialNumber =
            TxtSapMaterial.Text.Trim();

        _selectedPrintVersion.Title =
            TxtTitle.Text.Trim();

        _selectedPrintVersion.Customer =
            TxtCustomer.Text.Trim();

        _selectedPrintVersion.DecorationCode =
            TxtDecoration.Text.Trim();

        _selectedPrintVersion.ColorType =
            TxtColorType.Text.Trim();

        _selectedPrintVersion.GlassTreatment =
            TxtGlassTreatment.Text.Trim();

        _selectedPrintVersion.QualityClass =
            TxtQualityClass.Text.Trim();

        _selectedPrintVersion.HdNumber =
            TxtHdNumber.Text.Trim();

        _selectedPrintVersion.SampleLocation =
            TxtSampleLocation.Text.Trim();

        _selectedPrintVersion.BoardLocation =
            TxtBoardLocation.Text.Trim();

        _selectedPrintVersion.GaugeLocation =
            TxtGaugeLocation.Text.Trim();

        _selectedPrintVersion.HasGauge =
            ChkHasGauge.IsChecked == true;

        _selectedPrintVersion.HasComplaint =
            ChkComplaint.IsChecked == true;

        _selectedPrintVersion.SamplesOnCamera =
            ChkSamplesOnCamera.IsChecked == true;

        _selectedPrintVersion.Notes =
            TxtPrintVersionNotes.Text;

        if (_model is not null)
        {
            _model.ImportantInfo =
                TxtImportantInfo.Text;

            _model.ArticleNotes =
                TxtArticleNotes.Text;
        }
    }

    // ============================================================
    // ULOŽENÍ
    // ============================================================

    private void BtnSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TrySave())
        {
            return;
        }

        var target =
            _selectedPrintVersion?.FullPrintVersionNumber
            ?? _query;

        _allowNavigationAfterSave = true;

        TransactionRequested?.Invoke($"QA03 {target}");
    }

    private bool TrySave()
    {
        if (_model is null ||
            _selectedPrintVersion is null)
        {
            MessageBox.Show(
                "Není vybraná tisková verze k uložení.",
                "QA02",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        WriteFormToSelectedPrintVersion();

        var result = _service.Save(
            _model,
            _selectedPrintVersion);

        MessageBox.Show(
            result.Message,
            result.Success
                ? "QA02 - uloženo"
                : "QA02 - chyba",
            MessageBoxButton.OK,
            result.Success
                ? MessageBoxImage.Information
                : MessageBoxImage.Error);

        if (!result.Success)
        {
            return false;
        }

        ClearDirtyState();
        UpdateTaskStatus();

        return true;
    }

    // ============================================================
    // NAVIGACE
    // ============================================================

    private void BtnBackToQa03_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ConfirmNavigationAway())
        {
            return;
        }

        var target =
            _selectedPrintVersion?.FullPrintVersionNumber
            ?? _query;

        _allowNavigationAfterSave = true;

        TransactionRequested?.Invoke($"QA03 {target}");
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = DmsConfirmDialog.Show(
                Window.GetWindow(this),
                "Neuložené změny",
                "V QA02 jsou neuložené změny.\n\n" +
                "Chceš je před obnovením uložit?",
                showCancel: true);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes &&
                !TrySave())
            {
                return;
            }
        }

        LoadLookupData();
        LoadData();
    }

    public bool ConfirmNavigationAway()
    {
        if (_allowNavigationAfterSave)
        {
            _allowNavigationAfterSave = false;
            return true;
        }

        if (!_hasUnsavedChanges)
        {
            return true;
        }

        var result = DmsConfirmDialog.Show(
            Window.GetWindow(this),
            "Neuložené změny",
            "V QA02 jsou neuložené změny.\n\n" +
            "Chceš je před odchodem uložit?",
            showCancel: true);

        return result switch
        {
            MessageBoxResult.Yes => TrySave(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    // ============================================================
    // IDENTIFIKÁTORY
    // ============================================================

    private void BtnEnableIdentityEdit_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result = DmsConfirmDialog.Show(
            Window.GetWindow(this),
            "Změna identifikačních dat",
            "POZOR: Změna těchto polí může narušit integritu dat.\n\n" +
            "Opravdu chceš povolit změnu čísla tiskové verze a SAP ID?",
            showCancel: true);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        TxtPrintVersion.IsReadOnly = false;
        TxtSapMaterial.IsReadOnly = false;

        TxtPrintVersion.Focus();
    }

    private void IdentityField_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        MarkControlChanged(sender as Control);

        if (_identityWarningShown)
        {
            return;
        }
    }

    // ============================================================
    // DIRTY TRACKING
    // ============================================================

    private void EditableControl_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        MarkControlChanged(sender as Control);
    }

    private void EditableControl_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        MarkControlChanged(sender as Control);
    }

    private void GridTasks_CellEditEnding(
        object sender,
        DataGridCellEditEndingEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _hasUnsavedChanges = true;

        MarkGridChanged(GridTasks);

        Dispatcher.BeginInvoke(
            new Action(UpdateTaskStatus));
    }

    private void MarkControlChanged(Control? control)
    {
        if (control is null)
        {
            return;
        }

        _hasUnsavedChanges = true;

        _changedControls.Add(control);

        control.BorderBrush =
            new SolidColorBrush(
                Color.FromRgb(230, 160, 45));

        control.BorderThickness =
            new Thickness(2);
    }

    private static void MarkGridChanged(DataGrid grid)
    {
        grid.BorderBrush =
            new SolidColorBrush(
                Color.FromRgb(230, 160, 45));

        grid.BorderThickness =
            new Thickness(2);
    }

    private void ClearDirtyState()
    {
        _hasUnsavedChanges = false;

        foreach (var control in _changedControls)
        {
            control.ClearValue(
                Control.BorderBrushProperty);

            control.ClearValue(
                Control.BorderThicknessProperty);
        }

        _changedControls.Clear();

        GridTasks.ClearValue(
            Control.BorderBrushProperty);

        GridTasks.ClearValue(
            Control.BorderThicknessProperty);
    }

    // ============================================================
    // SCROLL
    // ============================================================

    private void GridTasks_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;

        RootScrollViewer.ScrollToVerticalOffset(
            RootScrollViewer.VerticalOffset - e.Delta);
    }

    // ============================================================
    // LOOKUPY / VÝBĚROVÁ POLE
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

    private void BtnApplyColorTypes_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selectedNames = _colorTypes
            .Where(item => ListColorTypes.SelectedItems.Contains(item))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .Select(item => item.Name)
            .ToList();

        TxtColorType.Text =
            string.Join(", ", selectedNames);

        PopupColorTypes.IsOpen = false;

        MarkControlChanged(TxtColorType);
    }

    private void BtnClearColorTypes_Click(
        object sender,
        RoutedEventArgs e)
    {
        ListColorTypes.SelectedItems.Clear();

        TxtColorType.Clear();

        PopupColorTypes.IsOpen = false;

        MarkControlChanged(TxtColorType);
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
            .Where(item => !string.IsNullOrWhiteSpace(item));
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

        MarkControlChanged(TxtCustomer);
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

        MarkControlChanged(TxtGlassTreatment);
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

        MarkControlChanged(TxtQualityClass);
    }

    // ============================================================
    // NORMALIZACE
    // ============================================================

    private static string NormalizeSapNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }

    // ============================================================
    // QUALITY ÚKOLY
    // ============================================================

    private void SubscribeTaskChanges()
    {
        if (_selectedPrintVersion is null)
        {
            return;
        }

        foreach (var task in _selectedPrintVersion.Tasks)
        {
            task.PropertyChanged -= Task_PropertyChanged;
            task.PropertyChanged += Task_PropertyChanged;

            _subscribedTasks.Add(task);
        }
    }

    private void UnsubscribeTaskChanges()
    {
        foreach (var task in _subscribedTasks)
        {
            task.PropertyChanged -= Task_PropertyChanged;
        }

        _subscribedTasks.Clear();
    }

    private void Task_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _hasUnsavedChanges = true;

        MarkGridChanged(GridTasks);

        UpdateTaskStatus();
    }

    private void UpdateTaskStatus()
    {
        if (_selectedPrintVersion is null)
        {
            TxtTaskStatus.Text =
                "Bez vybrané tiskové verze";

            return;
        }

        var tasks = _selectedPrintVersion.Tasks
            .Where(task =>
                !string.IsNullOrWhiteSpace(task.Text))
            .ToList();

        if (tasks.Count == 0)
        {
            TxtTaskStatus.Text = "Bez úkolů";
            return;
        }

        var completedCount = tasks.Count(task =>
            task.CompletedAt.HasValue);

        TxtTaskStatus.Text =
            completedCount == tasks.Count
                ? $"Úkoly splněny ({completedCount}/{tasks.Count})"
                : $"Úkoly nesplněny ({completedCount}/{tasks.Count})";
    }
}