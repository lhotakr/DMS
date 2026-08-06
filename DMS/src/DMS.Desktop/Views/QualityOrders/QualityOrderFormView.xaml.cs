using DMS.Core.Quality;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderFormView : UserControl
{
    private readonly string _dmsRootPath;
    private readonly bool _createMode;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly JsonQualityRepository _repository;
    private readonly QualityOrderMaintenanceService _service;

    private readonly List<SelectableOption> _machineOptions = new();
    private readonly List<SelectableOption> _colorTypeOptions = new();
    private readonly List<string> _qualityClassOptions = new();

    private QualityOrderFormModel _model = new();
    private QualityOrder? _snapshot;

    public event Action<string>? TransactionRequested;

    public QualityOrderFormView(
        string query,
        string dmsRootPath,
        bool createMode,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _dmsRootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;
        _createMode = createMode;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var paths = new QualityStoragePaths(_dmsRootPath);
        paths.EnsureDirectories();
        _repository = new JsonQualityRepository(paths);
        _service = new QualityOrderMaintenanceService(_repository);

        LoadReferenceData();
        ApplyLocalization();
        LoadInitial(query);
    }

    private void LoadReferenceData()
    {
        _machineOptions.Clear();

        try
        {
            var sapPaths = new SapStoragePaths(_dmsRootPath);
            var workCenters = new JsonSapWorkCenterRepository(sapPaths.SapWorkCentersFilePath)
                .LoadAll()
                // Quality orders are managed by local decoration/output-control processes.
                // Use only plant 2000 work centers; plant 9200 work centers would create
                // duplicate machine choices for QA/QO users.
                .Where(item => string.Equals(item.Plant?.Trim(), "2000", StringComparison.OrdinalIgnoreCase))
                .Where(item => !string.IsNullOrWhiteSpace(item.WorkCenter))
                .GroupBy(item => item.WorkCenter.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.DisplayText))
                    .ThenBy(item => item.DisplayText)
                    .First())
                .OrderBy(item => item.WorkCenter)
                .ToList();

            foreach (var workCenter in workCenters)
            {
                var displayText = string.IsNullOrWhiteSpace(workCenter.DisplayText)
                    ? workCenter.WorkCenter
                    : $"{workCenter.WorkCenter} - {workCenter.DisplayText}";

                _machineOptions.Add(new SelectableOption
                {
                    Code = workCenter.WorkCenter.Trim(),
                    DisplayText = displayText
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning($"QO machine option load failed: {ex.Message}");
        }

        LstMachines.ItemsSource = _machineOptions;

        _colorTypeOptions.Clear();
        _colorTypeOptions.AddRange(
            _repository.LoadColorTypes()
                .Where(item => item.IsActive && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .Select(item => new SelectableOption
                {
                    Code = item.Name.Trim(),
                    DisplayText = string.IsNullOrWhiteSpace(item.Code) ||
                                  string.Equals(item.Code.Trim(), item.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                        ? item.Name.Trim()
                        : $"{item.Code.Trim()} - {item.Name.Trim()}"
                })
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()));

        LstColorTypes.ItemsSource = _colorTypeOptions;

        _qualityClassOptions.Clear();
        _qualityClassOptions.AddRange(
            _repository.LoadQualityClasses()
                .Where(item => item.IsActive && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .Select(item => string.IsNullOrWhiteSpace(item.Code)
                    ? item.Name.Trim()
                    : $"{item.Code.Trim()} - {item.Name.Trim()}")
                .Distinct(StringComparer.OrdinalIgnoreCase));

        CmbQualityClass.ItemsSource = _qualityClassOptions;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = _createMode
            ? TF("QO01.Title", _model.OrderNumber)
            : TF("QO02.Title", _model.OrderNumber);
        TxtSubtitle.Text = _createMode
            ? T("QO01.Subtitle")
            : T("QO02.Subtitle");

        BtnSave.Content = _createMode
            ? T("QO01.Action.Create")
            : T("QO02.Action.Save");
        BtnOpenQO03.Content = T("QO.Action.OpenQO03");
        BtnOpenQO06.Content = T("QO.Action.OpenQO06");
        BtnOpenQO06.Visibility = _createMode ? Visibility.Collapsed : Visibility.Visible;
        BtnOpenQA03.Content = T("QO.Action.OpenTEC03");
        BtnReload.Content = T("QO.Action.Reload");
        BtnLoadPrintVersion.Content = T("QO.Action.LoadPrintVersion");

        TxtPrintVersionSection.Text = T("QO.Section.PrintVersion");
        LblPrintVersionInput.Text = T("QO.Field.PrintVersionInput");
        TxtPrintVersionHint.Text = T("QO.Hint.PrintVersionInput");
        LblPvNumber.Text = T("QO.Field.PrintVersion");
        LblSapId.Text = T("QO.Field.SapId");
        LblCustomer.Text = T("QO.Field.Customer");
        LblSampleLocation.Text = T("QO.Field.SampleLocation");
        LblBoardLocation.Text = T("QO.Field.BoardLocation");
        LblHdNumber.Text = T("QO.Field.HdNumber");
        LblGauge.Text = T("QO.Field.Gauge");
        LblCamera.Text = T("QO.Field.SamplesOnCamera");
        LblTasks.Text = T("QO.Field.TasksCompleted");
        LblPrintVersionNotes.Text = T("QO.Field.PrintVersionNotes");
        LblArticleData.Text = T("QO.Field.ArticleData");
        LblOpenTasks.Text = T("QO.Field.OpenTasks");

        TxtOrderSection.Text = T("QO.Section.OrderData");
        LblOrderNumber.Text = T("QO.Field.OrderNumberRequired");
        LblMachine.Text = T("QO.Field.MachineSapMultiSelect");
        TxtMachineHint.Text = (T("QO.Hint.MachineSapMultiSelect") + " " + T("QO.Hint.WorkCentersPlant2000")).Trim();
        LblColorType.Text = T("QO.Field.ColorTypeMultiSelect");
        TxtColorTypeHint.Text = T("QO.Hint.ColorTypeMultiSelect");
        LblQualityClass.Text = T("QO.Field.QualityClass");
        LblStart.Text = T("QO.Field.ProductionStart");
        LblEnd.Text = T("QO.Field.ProductionEnd");
        LblOrderedQuantity.Text = T("QO.Field.OrderedQuantity");
        LblProducedQuantity.Text = T("QO.Field.ProducedQuantity");
        LblLabOrder.Text = T("QO.Field.LabOrderNumber");
        LblLorealLabOrder.Text = T("QO.Field.LorealLabOrder");
        ChkLoreal.Content = T("QO.Flag.Loreal");
        ChkSortingInHd.Content = T("QO.Flag.SortingInHd");
        ChkStaysInHd.Content = T("QO.Flag.StaysInHd");
        LblReleaseState.Text = T("QO.Field.ReleaseState");
        LblScheduleStatus.Text = T("QO.Field.ScheduleStatus");
        LblOrderNotes.Text = T("QO.Field.OrderNotesImportant");
    }

    private void LoadInitial(string query)
    {
        _logger?.AdminAction(
            _createMode ? "QO01" : "QO02",
            _createMode ? "OpenQualityOrderCreate" : "OpenQualityOrderEdit",
            _currentUserName,
            $"Root={_dmsRootPath}; Query={query}");

        if (_createMode)
        {
            _model = _service.PrepareCreate(query);
            FillForm();

            if (string.IsNullOrWhiteSpace(_model.PrintVersionNumber) &&
                !string.IsNullOrWhiteSpace(query))
            {
                ShowWarning(TF("QO01.Warning.QueryNotPrintVersion", query));
            }

            return;
        }

        var model = _service.PrepareEdit(query);

        if (model is null)
        {
            var answer = DmsConfirmDialog.Show(
                Window.GetWindow(this),
                T("QO02.Dialog.NotFound.Title"),
                TF("QO02.Dialog.NotFound.Message", query),
                DmsDialogButtons.YesNo);

            if (answer == MessageBoxResult.Yes)
            {
                TransactionRequested?.Invoke($"QO01 {query}".Trim());
                return;
            }

            IsEnabled = false;
            ShowWarning(TF("QO02.Warning.NotFound", query));
            return;
        }

        _model = model;
        _snapshot = model.OriginalOrder;
        FillForm();
    }

    private void FillForm()
    {
        TxtTitle.Text = _createMode
            ? TF("QO01.Title", NullDash(_model.OrderNumber))
            : TF("QO02.Title", NullDash(_model.OrderNumber));

        TxtOrderNumber.Text = _model.OrderNumber;
        TxtOrderNumber.IsReadOnly = !_createMode;

        TxtPrintVersionInput.Text = _model.PrintVersionNumber;
        TxtPvNumber.Text = NullDash(_model.PrintVersionNumber);
        TxtSapId.Text = NullDash(_model.SapMaterialNumber);
        TxtCustomer.Text = NullDash(_model.Customer);
        TxtSampleLocation.Text = NullDash(_model.SampleLocation);
        TxtBoardLocation.Text = NullDash(_model.BoardLocation);
        TxtHdNumber.Text = NullDash(_model.HdNumber);
        TxtGauge.Text = BuildGaugeText(_model);
        TxtCamera.Text = ToYesNo(_model.SamplesOnCamera);
        TxtTasks.Text = _model.AllTasksCompleted
            ? $"✓ {T("QO.Text.TasksCompleted")} ({_model.TaskSummary})"
            : $"⚠ {T("QO.Text.TasksOpen")} ({_model.TaskSummary})";
        TxtTasks.Foreground = _model.AllTasksCompleted
            ? Brushes.LightGreen
            : Brushes.Orange;
        TxtPrintVersionNotes.Text = _model.PrintVersionNotes;
        TxtArticleData.Text = BuildArticleDataText(_model);
        TxtOpenTasks.Text = string.IsNullOrWhiteSpace(_model.OpenTasksText)
            ? T("QO.Text.NoOpenTasks")
            : _model.OpenTasksText;
        TxtOpenTasks.Foreground = _model.OpenTasks.Count == 0
            ? Brushes.LightGreen
            : Brushes.Orange;

        SelectMachinesFromModel();
        SelectColorTypesFromModel();
        SetQualityClassSelection(_model.QualityClass);
        DateStart.SelectedDate = _model.ProductionStart;
        DateEnd.SelectedDate = _model.ProductionEnd;
        TxtOrderedQuantity.Text = _model.OrderedQuantity?.ToString() ?? string.Empty;
        TxtProducedQuantity.Text = _model.ProducedQuantity?.ToString() ?? string.Empty;
        TxtLabOrder.Text = _model.LabOrderNumber;
        TxtLorealLabOrder.Text = _model.LorealLabOrder;
        ChkLoreal.IsChecked = _model.Loreal;
        TxtReleaseState.Text = T($"QO.Release.{_model.ReleaseStatusCode}");
        TxtScheduleStatus.Text = T($"QO.Status.{_model.ScheduleStatusCode}");
        TxtScheduleStatus.Foreground = _model.ScheduleStatusCode == "Finished"
            ? Brushes.LightGreen
            : _model.ScheduleStatusCode == "Scheduled"
                ? Brushes.Orange
                : Brushes.IndianRed;
        TxtReleaseState.Foreground = _model.Released
            ? Brushes.LightGreen
            : Brushes.Orange;
        ChkSortingInHd.IsChecked = _model.SortingInHd;
        ChkStaysInHd.IsChecked = _model.StaysInHd;
        TxtOrderNotes.Text = _model.Notes;

        DateEnd.IsEnabled = !_createMode;
        TxtProducedQuantity.IsReadOnly = _createMode;
        TxtProducedQuantity.IsEnabled = !_createMode;
    }

    private void BtnLoadPrintVersion_Click(object sender, RoutedEventArgs e)
    {
        LoadPrintVersionFromInput();
    }

    private void LoadPrintVersionFromInput()
    {
        ClearWarning();

        var query = TxtPrintVersionInput.Text.Trim();
        var printVersion = _service.FindPrintVersion(query);

        if (printVersion is null)
        {
            ShowWarning(TF("QO.Warning.PrintVersionNotFound", query));
            _logger?.AdminAction(
                _createMode ? "QO01" : "QO02",
                "PrintVersionNotFound",
                _currentUserName,
                $"Query={query}");
            return;
        }

        var currentOrderNumber = TxtOrderNumber.Text;
        var currentNotes = TxtOrderNotes.Text;
        var currentStart = DateStart.SelectedDate;
        var currentEnd = DateEnd.SelectedDate;
        var currentOrdered = TxtOrderedQuantity.Text;
        var currentProduced = TxtProducedQuantity.Text;
        var currentMachines = BuildSelectedMachineText();
        var currentColorTypes = BuildSelectedColorTypeText();
        var currentLab = TxtLabOrder.Text;
        var currentLorealLab = TxtLorealLabOrder.Text;
        var currentSorting = ChkSortingInHd.IsChecked == true;
        var currentStays = ChkStaysInHd.IsChecked == true;
        var currentReleased = _model.Released;
        var originalOrder = _model.OriginalOrder;

        _model = _service.PrepareCreate(printVersion.FullPrintVersionNumber);
        _model.IsCreateMode = _createMode;
        _model.Released = currentReleased;
        _model.OriginalOrder = originalOrder;
        _model.OrderNumber = currentOrderNumber;
        _model.Notes = currentNotes;
        _model.ProductionStart = currentStart;
        _model.ProductionEnd = currentEnd;
        _model.OrderedQuantity = TryParseInt(currentOrdered);
        _model.ProducedQuantity = TryParseInt(currentProduced);
        _model.Machine = currentMachines;
        _model.ColorType = currentColorTypes;
        _model.LabOrderNumber = currentLab;
        _model.LorealLabOrder = currentLorealLab;
        _model.SortingInHd = currentSorting;
        _model.StaysInHd = currentStays;
        _model.Finished = currentStart.HasValue && currentEnd.HasValue;
        _model.ScheduleStatusCode = !currentStart.HasValue
            ? "Unplanned"
            : currentEnd.HasValue
                ? "Finished"
                : "Scheduled";
        _model.ReleaseStatusCode = _model.Released ? "Released" : "Blocked";

        FillForm();
    }

    private void TxtOrderNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_createMode)
        {
            return;
        }

        var orderNumber = TxtOrderNumber.Text.Trim();

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return;
        }

        if (_service.FindOrder(orderNumber) is not null)
        {
            ShowWarning(TF("QO01.Warning.OrderNumberExistsInline", orderNumber));
            _logger?.AdminAction(
                "QO01",
                "OrderNumberDuplicateInlineWarning",
                _currentUserName,
                $"Order={orderNumber}");
            return;
        }

        ClearWarning();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        TrySave();
    }

    private bool TrySave()
    {
        ClearWarning();

        WriteFormToModel();

        if (_createMode &&
            _service.FindOrder(_model.OrderNumber) is not null)
        {
            ShowWarning(TF("QO01.Warning.OrderNumberExistsInline", _model.OrderNumber));

            var answer = DmsConfirmDialog.Show(
                Window.GetWindow(this),
                T("QO01.Dialog.Exists.Title"),
                TF("QO01.Dialog.Exists.Message", _model.OrderNumber),
                DmsDialogButtons.YesNo);

            if (answer == MessageBoxResult.Yes)
            {
                TransactionRequested?.Invoke($"QO02 {_model.OrderNumber}");
            }

            return false;
        }

        var result = _service.Save(_model, _currentUserName);

        if (!result.Success || result.SavedOrder is null)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("QO.Dialog.SaveFailed.Title"),
                result.Message);
            return false;
        }

        LogAudit(result.SavedOrder);

        _logger?.AdminAction(
            _createMode ? "QO01" : "QO02",
            _createMode ? "CreateQualityOrder" : "SaveQualityOrder",
            _currentUserName,
            $"Order={result.SavedOrder.OrderNumber}; PrintVersion={result.SavedOrder.PrintVersionNumber}; SapMaterial={result.SavedOrder.SapMaterialNumber}");

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("QO.Dialog.Saved.Title"),
            TF("QO.Dialog.Saved.Message", result.SavedOrder.OrderNumber));

        TransactionRequested?.Invoke($"QO03 {result.SavedOrder.OrderNumber}");
        return true;
    }

    private void WriteFormToModel()
    {
        _model.OrderNumber = TxtOrderNumber.Text.Trim();
        _model.PrintVersionNumber = TxtPrintVersionInput.Text.Trim();
        _model.SapMaterialNumber = TxtSapId.Text.Trim() == "-" ? string.Empty : TxtSapId.Text.Trim();
        _model.Machine = BuildSelectedMachineText();
        _model.ColorType = BuildSelectedColorTypeText();
        _model.QualityClass = (CmbQualityClass.Text ?? string.Empty).Trim();
        _model.ProductionStart = DateStart.SelectedDate;
        _model.ProductionEnd = DateEnd.SelectedDate;
        _model.OrderedQuantity = TryParseInt(TxtOrderedQuantity.Text);
        _model.ProducedQuantity = TryParseInt(TxtProducedQuantity.Text);
        _model.LabOrderNumber = TxtLabOrder.Text.Trim();
        _model.LorealLabOrder = TxtLorealLabOrder.Text.Trim();
        _model.Loreal = ChkLoreal.IsChecked == true;
        _model.SortingInHd = ChkSortingInHd.IsChecked == true;
        _model.StaysInHd = ChkStaysInHd.IsChecked == true;
        _model.Finished = _model.ProductionStart.HasValue && _model.ProductionEnd.HasValue;
        _model.ScheduleStatusCode = !_model.ProductionStart.HasValue
            ? "Unplanned"
            : _model.ProductionEnd.HasValue
                ? "Finished"
                : "Scheduled";
        _model.ReleaseStatusCode = _model.Released ? "Released" : "Blocked";
        _model.Notes = TxtOrderNotes.Text;
    }

    private void BtnOpenQO03_Click(object sender, RoutedEventArgs e)
    {
        var orderNumber = TxtOrderNumber.Text.Trim();

        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            TransactionRequested?.Invoke($"QO03 {orderNumber}");
        }
    }


    private void BtnOpenQO06_Click(object sender, RoutedEventArgs e)
    {
        var orderNumber = TxtOrderNumber.Text.Trim();

        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            TransactionRequested?.Invoke($"QO06 {orderNumber}");
        }
    }

    private void BtnOpenQA03_Click(object sender, RoutedEventArgs e)
    {
        var target = !string.IsNullOrWhiteSpace(_model.SapMaterialNumber)
            ? _model.SapMaterialNumber
            : TxtSapId.Text.Trim();

        if (string.Equals(target, "-", StringComparison.OrdinalIgnoreCase))
        {
            target = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            TransactionRequested?.Invoke($"TEC03 {target}");
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        LoadInitial(_createMode ? TxtPrintVersionInput.Text : TxtOrderNumber.Text);
    }

    private void SelectMachinesFromModel()
    {
        var tokens = SplitMultiValue(_model.Machine)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var option in _machineOptions)
        {
            option.IsSelected = tokens.Contains(option.Code) ||
                                tokens.Contains(option.DisplayText);
        }

        if (_machineOptions.Count == 0 && !string.IsNullOrWhiteSpace(_model.Machine))
        {
            _machineOptions.AddRange(
                SplitMultiValue(_model.Machine)
                    .Select(value => new SelectableOption
                    {
                        Code = value,
                        DisplayText = value,
                        IsSelected = true
                    }));
        }

        LstMachines.Items.Refresh();
    }

    private string BuildSelectedMachineText()
    {
        var selected = _machineOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Code)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(";#", selected);
    }

    private void SelectColorTypesFromModel()
    {
        var tokens = SplitMultiValue(_model.ColorType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var option in _colorTypeOptions)
        {
            option.IsSelected = tokens.Contains(option.Code) ||
                                tokens.Contains(option.DisplayText);
        }

        if (_colorTypeOptions.Count == 0 && !string.IsNullOrWhiteSpace(_model.ColorType))
        {
            _colorTypeOptions.AddRange(
                SplitMultiValue(_model.ColorType)
                    .Select(value => new SelectableOption
                    {
                        Code = value,
                        DisplayText = value,
                        IsSelected = true
                    }));
        }

        LstColorTypes.Items.Refresh();
    }

    private string BuildSelectedColorTypeText()
    {
        var selected = _colorTypeOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Code)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(";#", selected);
    }

    private void SetQualityClassSelection(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(normalized) &&
            !_qualityClassOptions.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            _qualityClassOptions.Add(normalized);
            CmbQualityClass.Items.Refresh();
        }

        CmbQualityClass.Text = normalized;
    }

    private void LogAudit(QualityOrder saved)
    {
        if (_createMode || _snapshot is null)
        {
            _logger?.AuditCreated(
                "QUALITY",
                "QualityOrder",
                saved.OrderNumber,
                _currentUserName,
                BuildOrderAuditDetail(saved));
            return;
        }

        AuditChange(saved, "PrintVersionNumber", _snapshot.PrintVersionNumber, saved.PrintVersionNumber);
        AuditChange(saved, "SapMaterialNumber", _snapshot.SapMaterialNumber, saved.SapMaterialNumber);
        AuditChange(saved, "Machine", _snapshot.Machine, saved.Machine);
        AuditChange(saved, "ColorType", _snapshot.ColorType, saved.ColorType);
        AuditChange(saved, "ProductionStart", FormatDate(_snapshot.ProductionStart), FormatDate(saved.ProductionStart));
        AuditChange(saved, "ProductionEnd", FormatDate(_snapshot.ProductionEnd), FormatDate(saved.ProductionEnd));
        AuditChange(saved, "OrderedQuantity", _snapshot.OrderedQuantity?.ToString(), saved.OrderedQuantity?.ToString());
        AuditChange(saved, "ProducedQuantity", _snapshot.ProducedQuantity?.ToString(), saved.ProducedQuantity?.ToString());
        AuditChange(saved, "QualityClass", _snapshot.QualityClass, saved.QualityClass);
        AuditChange(saved, "LabOrderNumber", _snapshot.LabOrderNumber, saved.LabOrderNumber);
        AuditChange(saved, "LorealLabOrder", _snapshot.LorealLabOrder, saved.LorealLabOrder);
        AuditChange(saved, "Loreal", _snapshot.Loreal.ToString(), saved.Loreal.ToString());
        AuditChange(saved, "SortingInHd", _snapshot.SortingInHd.ToString(), saved.SortingInHd.ToString());
        AuditChange(saved, "StaysInHd", _snapshot.StaysInHd.ToString(), saved.StaysInHd.ToString());
        AuditChange(saved, "Finished", _snapshot.Finished.ToString(), saved.Finished.ToString());
        AuditChange(saved, "Notes", _snapshot.Notes, saved.Notes);
    }

    private void AuditChange(QualityOrder saved, string field, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "QUALITY",
            "QualityOrder",
            saved.OrderNumber,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private static string BuildOrderAuditDetail(QualityOrder order)
    {
        return $"Order={order.OrderNumber}; PrintVersion={order.PrintVersionNumber}; SapMaterial={order.SapMaterialNumber}; Machine={order.Machine}; Quantity={order.OrderedQuantity}; Released={order.Released}; Status={QualityOrderMaintenanceService.GetScheduleStatusCode(order)}; Loreal={order.Loreal}; Notes={order.Notes}";
    }

    private void ShowWarning(string text)
    {
        TxtWarning.Text = text;
        WarningPanel.Visibility = Visibility.Visible;
    }

    private void ClearWarning()
    {
        TxtWarning.Text = string.Empty;
        WarningPanel.Visibility = Visibility.Collapsed;
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
            var value = _translateFormat(key, args);
            return IsMissing(value, key) ? key : value;
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

    private static bool IsMissing(string value, string key)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private string ToYesNo(bool value)
    {
        return value ? T("Common.Yes") : T("Common.No");
    }

    private string BuildArticleDataText(QualityOrderFormModel model)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(model.LegacyArticleNumber))
        {
            lines.Add($"{T("QO.Field.LegacyArticle")}: {model.LegacyArticleNumber}");
        }

        if (!string.IsNullOrWhiteSpace(model.ArticleTitle))
        {
            lines.Add($"{T("QO.Field.ArticleTitle")}: {model.ArticleTitle}");
        }

        if (!string.IsNullOrWhiteSpace(model.ArticleImportantInfo))
        {
            lines.Add($"{T("QO.Field.ArticleImportantInfo")}: {model.ArticleImportantInfo}");
        }

        if (!string.IsNullOrWhiteSpace(model.ArticleNotes))
        {
            lines.Add($"{T("QO.Field.ArticleNotes")}: {model.ArticleNotes}");
        }

        return lines.Count == 0
            ? T("QO.Text.NoArticleData")
            : string.Join(Environment.NewLine, lines);
    }

    private string BuildGaugeText(QualityOrderFormModel model)
    {
        if (!model.HasGauge && string.IsNullOrWhiteSpace(model.GaugeLocation))
        {
            return T("Common.No");
        }

        if (string.IsNullOrWhiteSpace(model.GaugeLocation))
        {
            return model.HasGauge ? T("Common.Yes") : T("Common.No");
        }

        return model.HasGauge
            ? $"{T("Common.Yes")} - {model.GaugeLocation}"
            : model.GaugeLocation;
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value?.Trim(), out var result)
            ? result
            : null;
    }

    private static string FormatDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd") ?? string.Empty;
    }

    private static IReadOnlyList<string> SplitMultiValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(new[] { ";#", ";", "," }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class SelectableOption
    {
        public string Code { get; init; } = string.Empty;

        public string DisplayText { get; init; } = string.Empty;

        public bool IsSelected { get; set; }
    }
}
