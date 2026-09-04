using DMS.Core.Quality;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
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
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private QualityArticleEditModel? _model;
    private QualityPrintVersionEditModel? _selectedPrintVersion;

    private ArticleSnapshot? _articleSnapshot;
    private PrintVersionSnapshot? _printVersionSnapshot;

    private bool _identityWarningShown;
    private bool _isLoading;
    private bool _hasUnsavedChanges;
    private bool _allowNavigationAfterSave;

    private readonly HashSet<Control> _changedControls = new();
    private readonly List<QualityTaskEditModel> _subscribedTasks = new();

    public event Action<string>? TransactionRequested;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public QualityArticleEditView(string query)
        : this(
            query,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            null,
            null,
            null,
            null)
    {
    }

    public QualityArticleEditView(
        string query,
        string dmsRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _query = query;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var paths = new QualityStoragePaths(rootPath);
        paths.EnsureDirectories();

        _repository = new JsonQualityRepository(paths);
        _service = new QualityArticleEditService(_repository);

        ApplyLocalization();

        _logger?.AdminAction(
            "QA02",
            "OpenQualityArticleEdit",
            _currentUserName,
            $"Root={rootPath}; Query={query}");

        LoadLookupData();
        LoadData();
    }

    private void ApplyLocalization()
    {
        TxtPageTitle.Text = T("QA02.Title");
        BtnSave.Content = T("QA02.Action.Save");
        BtnBackToQa03.Content = T("QA02.Action.BackToQa03");
        BtnReload.Content = T("QA02.Action.Reload");

        LblSelectedPrintVersion.Text = T("QA02.Field.SelectedPrintVersion");
        TxtSectionArticleInfo.Text = T("QA02.Section.ArticleInfo");
        LblImportantInfo.Text = T("QA02.Field.ImportantInfo");
        LblArticleNotes.Text = T("QA02.Field.ArticleNotes");

        BtnEnableIdentityEdit.Content = T("QA02.Action.EnableIdentityEdit");
        TxtSectionPrintVersion.Text = T("QA02.Section.PrintVersion");
        LblPrintVersionNumber.Text = T("QA02.Field.PrintVersionNumber");
        LblSapMaterial.Text = T("QA02.Field.SapId");
        LblTitle.Text = T("QA02.Field.PrintVersionTitle");
        LblCustomer.Text = T("QA02.Field.Customer");
        LblDecoration.Text = T("QA02.Field.Decoration");
        TxtDecoration.ToolTip = T("QA02.Tooltip.DecorationFromSap");
        LblColorType.Text = T("QA02.Field.ColorType");
        TxtColorType.ToolTip = T("QA02.Tooltip.MultipleValues");
        BtnClearColorTypes.Content = T("QA02.Action.Clear");
        BtnApplyColorTypes.Content = T("QA02.Action.Apply");
        LblGlassTreatment.Text = T("QA02.Field.GlassTreatment");
        LblHdNumber.Text = T("QA02.Field.HdNumber");
        LblSampleLocation.Text = T("QA02.Field.SampleLocation");
        LblBoardLocation.Text = T("QA02.Field.BoardLocation");
        LblGaugeLocation.Text = T("QA02.Field.GaugeLocation");
        LblQualityClass.Text = T("QA02.Field.QualityClass");
        ChkHasGauge.Content = T("QA02.Flag.HasGauge");
        ChkComplaint.Content = T("QA02.Flag.Complaint");
        ChkSamplesOnCamera.Content = T("QA02.Flag.SamplesOnCamera");
        LblPrintVersionNotes.Text = T("QA02.Field.Notes");

        TxtSectionTasks.Text = T("QA02.Section.Tasks");
        ColTaskNumber.Header = T("QA02.Task.Column.Number");
        ColTaskText.Header = T("QA02.Task.Column.Task");
        ColTaskDueDate.Header = T("QA02.Task.Column.DueDate");
        ColTaskCompleted.Header = T("QA02.Task.Column.Completed");
        ColTaskCompletedAt.Header = T("QA02.Task.Column.CompletedAt");
    }

    private void LoadData()
    {
        _isLoading = true;

        try
        {
            _model = _service.Load(_query);

            if (_model is null)
            {
                DmsConfirmDialog.ShowInfo(
                    Window.GetWindow(this),
                    T("QA02.Dialog.NotFound.Title"),
                    TF("QA02.Dialog.NotFound.Message", _query));

                _logger?.AdminAction(
                    "QA02",
                    "QualityDataNotFound",
                    _currentUserName,
                    $"Query={_query}");

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
            UpdateSnapshots();
            _identityWarningShown = false;

            _logger?.AdminAction(
                "QA02",
                "LoadQualityArticleEdit",
                _currentUserName,
                $"Query={_query}; PrintVersions={_model.PrintVersions.Count}");
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

            TxtPrintVersion.Text = _selectedPrintVersion.FullPrintVersionNumber;
            TxtSapMaterial.Text = _selectedPrintVersion.SapMaterialNumber;
            TxtTitle.Text = _selectedPrintVersion.Title;
            TxtCustomer.Text = _selectedPrintVersion.Customer;
            TxtDecoration.Text = _selectedPrintVersion.DecorationCode;
            TxtColorType.Text = _selectedPrintVersion.ColorType;
            TxtGlassTreatment.Text = _selectedPrintVersion.GlassTreatment;
            TxtQualityClass.Text = _selectedPrintVersion.QualityClass;
            TxtHdNumber.Text = _selectedPrintVersion.HdNumber;
            TxtSampleLocation.Text = _selectedPrintVersion.SampleLocation;
            TxtBoardLocation.Text = _selectedPrintVersion.BoardLocation;
            TxtGaugeLocation.Text = _selectedPrintVersion.GaugeLocation;
            ChkHasGauge.IsChecked = _selectedPrintVersion.HasGauge;
            ChkComplaint.IsChecked = _selectedPrintVersion.HasComplaint;
            ChkSamplesOnCamera.IsChecked = _selectedPrintVersion.SamplesOnCamera;
            TxtPrintVersionNotes.Text = _selectedPrintVersion.Notes;

            UnsubscribeTaskChanges();
            GridTasks.ItemsSource = _selectedPrintVersion.Tasks;
            SubscribeTaskChanges();
            UpdateTaskStatus();

            TxtPrintVersion.IsReadOnly = true;
            TxtSapMaterial.IsReadOnly = true;
            TxtDecoration.IsReadOnly = true;

            ClearDirtyState();
            UpdateSnapshots();
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
        TxtTaskStatus.Text = T("QA02.TaskStatus.NoPrintVersion");
    }

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
                T("QA02.Dialog.Unsaved.Title"),
                T("QA02.Dialog.Unsaved.SwitchMessage"),
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
            ListPrintVersions.SelectedItem = _selectedPrintVersion;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void WriteFormToSelectedPrintVersion()
    {
        if (_selectedPrintVersion is null)
        {
            return;
        }

        GridTasks.CommitEdit(DataGridEditingUnit.Cell, true);
        GridTasks.CommitEdit(DataGridEditingUnit.Row, true);

        _selectedPrintVersion.FullPrintVersionNumber = TxtPrintVersion.Text.Trim();
        _selectedPrintVersion.SapMaterialNumber = TxtSapMaterial.Text.Trim();
        _selectedPrintVersion.Title = TxtTitle.Text.Trim();
        _selectedPrintVersion.Customer = TxtCustomer.Text.Trim();
        _selectedPrintVersion.DecorationCode = TxtDecoration.Text.Trim();
        _selectedPrintVersion.ColorType = TxtColorType.Text.Trim();
        _selectedPrintVersion.GlassTreatment = TxtGlassTreatment.Text.Trim();
        _selectedPrintVersion.QualityClass = TxtQualityClass.Text.Trim();
        _selectedPrintVersion.HdNumber = TxtHdNumber.Text.Trim();
        _selectedPrintVersion.SampleLocation = TxtSampleLocation.Text.Trim();
        _selectedPrintVersion.BoardLocation = TxtBoardLocation.Text.Trim();
        _selectedPrintVersion.GaugeLocation = TxtGaugeLocation.Text.Trim();
        _selectedPrintVersion.HasGauge = ChkHasGauge.IsChecked == true;
        _selectedPrintVersion.HasComplaint = ChkComplaint.IsChecked == true;
        _selectedPrintVersion.SamplesOnCamera = ChkSamplesOnCamera.IsChecked == true;
        _selectedPrintVersion.Notes = TxtPrintVersionNotes.Text;

        if (_model is not null)
        {
            _model.ImportantInfo = TxtImportantInfo.Text;
            _model.ArticleNotes = TxtArticleNotes.Text;
        }
    }

    private void BtnSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TrySave())
        {
            return;
        }

        var target = _selectedPrintVersion?.FullPrintVersionNumber ?? _query;

        _allowNavigationAfterSave = true;
        TransactionRequested?.Invoke($"QA03 {target}");
    }

    private bool TrySave()
    {
        if (_model is null ||
            _selectedPrintVersion is null)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("QA02.Dialog.Validation.Title"),
                T("QA02.Validation.NoPrintVersionSelected"));

            return false;
        }

        WriteFormToSelectedPrintVersion();

        var result = _service.Save(
            _model,
            _selectedPrintVersion);

        if (!result.Success)
        {
            _logger?.AdminAction(
                "QA02",
                "SaveQualityArticleFailed",
                _currentUserName,
                $"PrintVersion={_selectedPrintVersion.FullPrintVersionNumber}; Message={result.Message}");

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("QA02.Dialog.SaveFailed.Title"),
                TF("QA02.Dialog.SaveFailed.Message", result.Message));

            return false;
        }

        LogQualityChanges();

        _logger?.AdminAction(
            "QA02",
            "SaveQualityArticleEdit",
            _currentUserName,
            $"PrintVersion={_selectedPrintVersion.FullPrintVersionNumber}; SapMaterial={_selectedPrintVersion.SapMaterialNumber}");

        ClearDirtyState();
        UpdateTaskStatus();
        UpdateSnapshots();

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("QA02.Dialog.Saved.Title"),
            TF("QA02.Dialog.Saved.Message", _selectedPrintVersion.FullPrintVersionNumber));

        return true;
    }

    private void BtnBackToQa03_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ConfirmNavigationAway())
        {
            return;
        }

        var target = _selectedPrintVersion?.FullPrintVersionNumber ?? _query;

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
                T("QA02.Dialog.Unsaved.Title"),
                T("QA02.Dialog.Unsaved.ReloadMessage"),
                showCancel: true);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes && !TrySave())
            {
                return;
            }
        }

        _logger?.AdminAction(
            "QA02",
            "ReloadQualityArticleEdit",
            _currentUserName,
            $"Query={_query}; HadUnsavedChanges={_hasUnsavedChanges}");

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
            T("QA02.Dialog.Unsaved.Title"),
            T("QA02.Dialog.Unsaved.LeaveMessage"),
            showCancel: true);

        return result switch
        {
            MessageBoxResult.Yes => TrySave(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void BtnEnableIdentityEdit_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result = DmsConfirmDialog.Show(
            Window.GetWindow(this),
            T("QA02.Dialog.Identity.Title"),
            T("QA02.Dialog.Identity.Message"),
            showCancel: true);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _logger?.AdminAction(
            "QA02",
            "EnableIdentityEdit",
            _currentUserName,
            $"PrintVersion={_selectedPrintVersion?.FullPrintVersionNumber ?? _query}");

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

        _identityWarningShown = true;
    }

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

        Dispatcher.BeginInvoke(new Action(UpdateTaskStatus));
    }

    private void MarkControlChanged(Control? control)
    {
        if (control is null)
        {
            return;
        }

        _hasUnsavedChanges = true;
        _changedControls.Add(control);

        control.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 160, 45));
        control.BorderThickness = new Thickness(2);
    }

    private static void MarkGridChanged(DataGrid grid)
    {
        grid.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 160, 45));
        grid.BorderThickness = new Thickness(2);
    }

    private void ClearDirtyState()
    {
        _hasUnsavedChanges = false;

        foreach (var control in _changedControls)
        {
            control.ClearValue(Control.BorderBrushProperty);
            control.ClearValue(Control.BorderThicknessProperty);
        }

        _changedControls.Clear();

        GridTasks.ClearValue(Control.BorderBrushProperty);
        GridTasks.ClearValue(Control.BorderThicknessProperty);
    }

    private void GridTasks_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        e.Handled = true;

        RootScrollViewer.ScrollToVerticalOffset(
            RootScrollViewer.VerticalOffset - e.Delta);
    }

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
        SelectColorTypesFromText(TxtColorType.Text);
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

        TxtColorType.Text = string.Join(", ", selectedNames);
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

    private void SelectColorTypesFromText(string? currentText)
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

    private static IEnumerable<string> SplitLookupValues(string? value)
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
            TxtTaskStatus.Text = T("QA02.TaskStatus.NoPrintVersion");
            return;
        }

        var tasks = _selectedPrintVersion.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Text))
            .ToList();

        if (tasks.Count == 0)
        {
            TxtTaskStatus.Text = T("QA02.TaskStatus.NoTasks");
            return;
        }

        var completedCount = tasks.Count(task => task.CompletedAt.HasValue);

        TxtTaskStatus.Text = completedCount == tasks.Count
            ? TF("QA02.TaskStatus.AllCompleted", completedCount, tasks.Count)
            : TF("QA02.TaskStatus.NotCompleted", completedCount, tasks.Count);
    }

    private void UpdateSnapshots()
    {
        _articleSnapshot = _model is null
            ? null
            : ArticleSnapshot.From(_model);

        _printVersionSnapshot = _selectedPrintVersion is null
            ? null
            : PrintVersionSnapshot.From(_selectedPrintVersion);
    }

    private void LogQualityChanges()
    {
        if (_model is not null && _articleSnapshot is not null)
        {
            var articleId = _selectedPrintVersion?.SapMaterialNumber ?? _query;

            LogFieldChange(
                "QualityArticle",
                articleId,
                "ImportantInfo",
                _articleSnapshot.ImportantInfo,
                _model.ImportantInfo);

            LogFieldChange(
                "QualityArticle",
                articleId,
                "ArticleNotes",
                _articleSnapshot.ArticleNotes,
                _model.ArticleNotes);
        }

        if (_selectedPrintVersion is null || _printVersionSnapshot is null)
        {
            return;
        }

        var current = _selectedPrintVersion;
        var old = _printVersionSnapshot;
        var entityId = current.FullPrintVersionNumber;

        LogFieldChange("QualityPrintVersion", entityId, "FullPrintVersionNumber", old.FullPrintVersionNumber, current.FullPrintVersionNumber);
        LogFieldChange("QualityPrintVersion", entityId, "SapMaterialNumber", old.SapMaterialNumber, current.SapMaterialNumber);
        LogFieldChange("QualityPrintVersion", entityId, "Title", old.Title, current.Title);
        LogFieldChange("QualityPrintVersion", entityId, "Customer", old.Customer, current.Customer);
        LogFieldChange("QualityPrintVersion", entityId, "DecorationCode", old.DecorationCode, current.DecorationCode);
        LogFieldChange("QualityPrintVersion", entityId, "ColorType", old.ColorType, current.ColorType);
        LogFieldChange("QualityPrintVersion", entityId, "GlassTreatment", old.GlassTreatment, current.GlassTreatment);
        LogFieldChange("QualityPrintVersion", entityId, "QualityClass", old.QualityClass, current.QualityClass);
        LogFieldChange("QualityPrintVersion", entityId, "HdNumber", old.HdNumber, current.HdNumber);
        LogFieldChange("QualityPrintVersion", entityId, "SampleLocation", old.SampleLocation, current.SampleLocation);
        LogFieldChange("QualityPrintVersion", entityId, "BoardLocation", old.BoardLocation, current.BoardLocation);
        LogFieldChange("QualityPrintVersion", entityId, "GaugeLocation", old.GaugeLocation, current.GaugeLocation);
        LogFieldChange("QualityPrintVersion", entityId, "HasGauge", old.HasGauge.ToString(), current.HasGauge.ToString());
        LogFieldChange("QualityPrintVersion", entityId, "HasComplaint", old.HasComplaint.ToString(), current.HasComplaint.ToString());
        LogFieldChange("QualityPrintVersion", entityId, "SamplesOnCamera", old.SamplesOnCamera.ToString(), current.SamplesOnCamera.ToString());
        LogFieldChange("QualityPrintVersion", entityId, "Notes", old.Notes, current.Notes);

        LogTaskChanges(old, current);
    }

    private void LogTaskChanges(
        PrintVersionSnapshot old,
        QualityPrintVersionEditModel current)
    {
        var oldTasks = old.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Number))
            .GroupBy(task => task.Number, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var currentTasks = current.Tasks
            .Select(TaskSnapshot.From)
            .Where(task => !string.IsNullOrWhiteSpace(task.Number))
            .GroupBy(task => task.Number, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var currentTask in currentTasks.Values)
        {
            var entityId = $"{current.FullPrintVersionNumber}#{currentTask.Number}";

            if (!oldTasks.TryGetValue(currentTask.Number, out var oldTask))
            {
                if (!string.IsNullOrWhiteSpace(currentTask.Text))
                {
                    _logger?.AuditCreated(
                        "QA02",
                        "QualityTask",
                        entityId,
                        _currentUserName,
                        currentTask.ToDetail());
                }

                continue;
            }

            if (oldTask.IsEmpty && currentTask.IsEmpty)
            {
                continue;
            }

            if (!oldTask.IsEmpty && currentTask.IsEmpty)
            {
                _logger?.AuditDeleted(
                    "QA02",
                    "QualityTask",
                    entityId,
                    _currentUserName,
                    oldTask.ToDetail());

                continue;
            }

            LogFieldChange("QualityTask", entityId, "Text", oldTask.Text, currentTask.Text);
            LogFieldChange("QualityTask", entityId, "DueDate", FormatDateForLog(oldTask.DueDate), FormatDateForLog(currentTask.DueDate));
            LogFieldChange("QualityTask", entityId, "IsCompleted", oldTask.IsCompleted.ToString(), currentTask.IsCompleted.ToString());
            LogFieldChange("QualityTask", entityId, "CompletedAt", FormatDateForLog(oldTask.CompletedAt), FormatDateForLog(currentTask.CompletedAt));
        }

        foreach (var oldTask in oldTasks.Values)
        {
            if (currentTasks.ContainsKey(oldTask.Number) || oldTask.IsEmpty)
            {
                continue;
            }

            var entityId = $"{current.FullPrintVersionNumber}#{oldTask.Number}";

            _logger?.AuditDeleted(
                "QA02",
                "QualityTask",
                entityId,
                _currentUserName,
                oldTask.ToDetail());
        }
    }

    private void LogFieldChange(
        string entity,
        string entityId,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "QA02",
            entity,
            entityId,
            field,
            oldValue,
            newValue,
            _currentUserName);
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
            var value = _translateFormat.Invoke(key, args);
            if (!IsMissing(value, key))
            {
                return value;
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

    private static string FormatDateForLog(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd")
            : string.Empty;
    }

    private sealed class ArticleSnapshot
    {
        public string ImportantInfo { get; init; } = string.Empty;
        public string ArticleNotes { get; init; } = string.Empty;

        public static ArticleSnapshot From(QualityArticleEditModel model)
        {
            return new ArticleSnapshot
            {
                ImportantInfo = model.ImportantInfo ?? string.Empty,
                ArticleNotes = model.ArticleNotes ?? string.Empty
            };
        }
    }

    private sealed class PrintVersionSnapshot
    {
        public string FullPrintVersionNumber { get; init; } = string.Empty;
        public string SapMaterialNumber { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Customer { get; init; } = string.Empty;
        public string DecorationCode { get; init; } = string.Empty;
        public string ColorType { get; init; } = string.Empty;
        public string GlassTreatment { get; init; } = string.Empty;
        public string QualityClass { get; init; } = string.Empty;
        public string HdNumber { get; init; } = string.Empty;
        public string SampleLocation { get; init; } = string.Empty;
        public string BoardLocation { get; init; } = string.Empty;
        public string GaugeLocation { get; init; } = string.Empty;
        public bool HasGauge { get; init; }
        public bool HasComplaint { get; init; }
        public bool SamplesOnCamera { get; init; }
        public string Notes { get; init; } = string.Empty;
        public List<TaskSnapshot> Tasks { get; init; } = new();

        public static PrintVersionSnapshot From(QualityPrintVersionEditModel model)
        {
            return new PrintVersionSnapshot
            {
                FullPrintVersionNumber = model.FullPrintVersionNumber ?? string.Empty,
                SapMaterialNumber = model.SapMaterialNumber ?? string.Empty,
                Title = model.Title ?? string.Empty,
                Customer = model.Customer ?? string.Empty,
                DecorationCode = model.DecorationCode ?? string.Empty,
                ColorType = model.ColorType ?? string.Empty,
                GlassTreatment = model.GlassTreatment ?? string.Empty,
                QualityClass = model.QualityClass ?? string.Empty,
                HdNumber = model.HdNumber ?? string.Empty,
                SampleLocation = model.SampleLocation ?? string.Empty,
                BoardLocation = model.BoardLocation ?? string.Empty,
                GaugeLocation = model.GaugeLocation ?? string.Empty,
                HasGauge = model.HasGauge,
                HasComplaint = model.HasComplaint,
                SamplesOnCamera = model.SamplesOnCamera,
                Notes = model.Notes ?? string.Empty,
                Tasks = model.Tasks.Select(TaskSnapshot.From).ToList()
            };
        }
    }

    private sealed class TaskSnapshot
    {
        public string Number { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public DateTime? DueDate { get; init; }
        public bool IsCompleted { get; init; }
        public DateTime? CompletedAt { get; init; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Text) &&
            !DueDate.HasValue &&
            !IsCompleted &&
            !CompletedAt.HasValue;

        public static TaskSnapshot From(QualityTaskEditModel model)
        {
            return new TaskSnapshot
            {
                Number = Convert.ToString(model.Number) ?? string.Empty,
                Text = model.Text ?? string.Empty,
                DueDate = model.DueDate,
                IsCompleted = model.IsCompleted,
                CompletedAt = model.CompletedAt
            };
        }

        public string ToDetail()
        {
            return $"Number={Number}; Text={Text}; DueDate={FormatDateForLog(DueDate)}; IsCompleted={IsCompleted}; CompletedAt={FormatDateForLog(CompletedAt)}";
        }
    }
}
