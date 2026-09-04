using DMS.Core.Common.Editing;
using DMS.Core.Quality;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualitySettingsView : UserControl, IUnsavedChangesGuard
{
    private readonly QualityStoragePaths _paths;
    private readonly JsonQualityRepository _repository;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private ObservableCollection<EditableRow<QualityCustomer>> _customers = new();
    private ObservableCollection<EditableRow<QualityLookupItem>> _colorTypes = new();
    private ObservableCollection<EditableRow<QualityLookupItem>> _glassTreatments = new();
    private ObservableCollection<EditableRow<QualityLookupItem>> _qualityClasses = new();

    private List<QualityCustomer> _originalCustomers = new();
    private List<QualityLookupItem> _originalColorTypes = new();
    private List<QualityLookupItem> _originalGlassTreatments = new();
    private List<QualityLookupItem> _originalQualityClasses = new();

    public bool HasUnsavedChanges =>
        _customers.Any(IsChanged) ||
        _colorTypes.Any(IsChanged) ||
        _glassTreatments.Any(IsChanged) ||
        _qualityClasses.Any(IsChanged);

    public QualitySettingsView()
        : this(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            null,
            null,
            null,
            null)
    {
    }

    public QualitySettingsView(
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

        var normalizedRootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : Path.GetFullPath(dmsRootPath);

        _paths = new QualityStoragePaths(normalizedRootPath);
        _paths.EnsureDirectories();

        _repository = new JsonQualityRepository(_paths);

        ApplyLocalization();
        EnsureDefaultLookupFiles();
        LoadData();

        _logger?.AdminAction(
            "QASET",
            "OpenQualitySettings",
            _currentUserName,
            $"BasePath={_paths.BasePath}; QualityPath={_paths.QualityPath}");
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("QASET.Dialog.UnsavedTitle"),
            T("QASET.Dialog.UnsavedMessage"));
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("QASET.Title");
        TxtSubtitle.Text = T("QASET.Subtitle");

        TabCustomers.Header = T("QASET.Tab.Customers");
        TabColorTypes.Header = T("QASET.Tab.ColorTypes");
        TabGlassTreatments.Header = T("QASET.Tab.GlassTreatments");
        TabQualityClasses.Header = T("QASET.Tab.QualityClasses");
        TabPaths.Header = T("QASET.Tab.Paths");

        SetButtonText(BtnAddCustomer, "QASET.Button.Add");
        SetButtonText(BtnDeleteCustomer, "QASET.Button.Delete");
        SetButtonText(BtnSaveCustomers, "QASET.Button.Save");
        SetButtonText(BtnReloadCustomers, "QASET.Button.Reload");

        SetButtonText(BtnAddColorType, "QASET.Button.Add");
        SetButtonText(BtnDeleteColorType, "QASET.Button.Delete");
        SetButtonText(BtnSaveColorTypes, "QASET.Button.Save");
        SetButtonText(BtnReloadColorTypes, "QASET.Button.Reload");

        SetButtonText(BtnAddGlassTreatment, "QASET.Button.Add");
        SetButtonText(BtnDeleteGlassTreatment, "QASET.Button.Delete");
        SetButtonText(BtnSaveGlassTreatments, "QASET.Button.Save");
        SetButtonText(BtnReloadGlassTreatments, "QASET.Button.Reload");

        SetButtonText(BtnAddQualityClass, "QASET.Button.Add");
        SetButtonText(BtnDeleteQualityClass, "QASET.Button.Delete");
        SetButtonText(BtnSaveQualityClasses, "QASET.Button.Save");
        SetButtonText(BtnReloadQualityClasses, "QASET.Button.Reload");

        ColCustomerName.Header = T("QASET.Column.Name");
        ColCustomerActive.Header = T("QASET.Column.Active");
        ColCustomerLoreal.Header = T("QASET.Column.Loreal");
        ColCustomerSourceId.Header = T("QASET.Column.SourceId");
        ColCustomerJsonCode.Header = T("QASET.Column.JsonCode");

        ApplyLookupColumnHeaders(ColColorCode, ColColorName, ColColorActive, ColColorSortOrder, ColColorNotes);
        ApplyLookupColumnHeaders(ColGlassCode, ColGlassName, ColGlassActive, ColGlassSortOrder, ColGlassNotes);
        ApplyLookupColumnHeaders(ColClassCode, ColClassName, ColClassActive, ColClassSortOrder, ColClassNotes);
    }

    private void ApplyLookupColumnHeaders(
        DataGridColumn codeColumn,
        DataGridColumn nameColumn,
        DataGridColumn activeColumn,
        DataGridColumn sortOrderColumn,
        DataGridColumn notesColumn)
    {
        codeColumn.Header = T("QASET.Column.Code");
        nameColumn.Header = T("QASET.Column.Name");
        activeColumn.Header = T("QASET.Column.Active");
        sortOrderColumn.Header = T("QASET.Column.SortOrder");
        notesColumn.Header = T("QASET.Column.Notes");
    }

    private void SetButtonText(Button button, string key)
    {
        button.Content = T(key);
    }

    private void LoadData()
    {
        _customers = new ObservableCollection<EditableRow<QualityCustomer>>(
            _repository
                .LoadCustomers()
                .OrderBy(item => item.Name)
                .Select(item => new EditableRow<QualityCustomer>(CloneCustomer(item))));

        _colorTypes = LoadLookupRows(_repository.LoadColorTypes());
        _glassTreatments = LoadLookupRows(_repository.LoadGlassTreatments());
        _qualityClasses = LoadLookupRows(_repository.LoadQualityClasses());

        _originalCustomers = _customers
            .Select(row => CloneCustomer(row.Item))
            .ToList();

        _originalColorTypes = _colorTypes
            .Select(row => CloneLookup(row.Item))
            .ToList();

        _originalGlassTreatments = _glassTreatments
            .Select(row => CloneLookup(row.Item))
            .ToList();

        _originalQualityClasses = _qualityClasses
            .Select(row => CloneLookup(row.Item))
            .ToList();

        GridCustomers.ItemsSource = _customers;
        GridColorTypes.ItemsSource = _colorTypes;
        GridGlassTreatments.ItemsSource = _glassTreatments;
        GridQualityClasses.ItemsSource = _qualityClasses;

        TxtPaths.Text =
            $"{T("QASET.Paths.BasePath")}: {_paths.BasePath}\n" +
            $"{T("QASET.Paths.QualityPath")}: {_paths.QualityPath}\n\n" +
            $"{T("QASET.Paths.Customers")}: {_paths.QualityCustomersFilePath}\n" +
            $"{T("QASET.Paths.ColorTypes")}: {_paths.QualityColorTypesFilePath}\n" +
            $"{T("QASET.Paths.GlassTreatments")}: {_paths.QualityGlassTreatmentsFilePath}\n" +
            $"{T("QASET.Paths.QualityClasses")}: {_paths.QualityClassesFilePath}";

        TxtStatus.Text = TF("QASET.Status.Ready",
            _customers.Count,
            _colorTypes.Count,
            _glassTreatments.Count,
            _qualityClasses.Count);

        _logger?.AdminAction(
            "QASET",
            "LoadQualitySettings",
            _currentUserName,
            $"Customers={_customers.Count}; ColorTypes={_colorTypes.Count}; GlassTreatments={_glassTreatments.Count}; QualityClasses={_qualityClasses.Count}");
    }

    private static ObservableCollection<EditableRow<QualityLookupItem>> LoadLookupRows(
        IReadOnlyList<QualityLookupItem> items)
    {
        return new ObservableCollection<EditableRow<QualityLookupItem>>(
            items
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .Select(item => new EditableRow<QualityLookupItem>(CloneLookup(item))));
    }

    private void BtnAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var row = new EditableRow<QualityCustomer>(
            new QualityCustomer
            {
                Code = string.Empty,
                Name = T("QASET.Default.NewCustomer"),
                IsActive = true,
                IsLoreal = false,
                SourceId = 0
            },
            EditableRowState.Added);

        _customers.Add(row);
        GridCustomers.SelectedItem = row;
        GridCustomers.ScrollIntoView(row);

        TxtStatus.Text = T("QASET.Status.CustomerAdded");

        LogAction("AddCustomer", "");
    }

    private void BtnAddColorType_Click(object sender, RoutedEventArgs e)
    {
        AddLookupRow(_colorTypes, GridColorTypes, "NEW_COLOR", T("QASET.Default.NewColorType"));
        TxtStatus.Text = T("QASET.Status.ColorTypeAdded");
        LogAction("AddColorType", "");
    }

    private void BtnAddGlassTreatment_Click(object sender, RoutedEventArgs e)
    {
        AddLookupRow(_glassTreatments, GridGlassTreatments, "NEW_GLASS", T("QASET.Default.NewGlassTreatment"));
        TxtStatus.Text = T("QASET.Status.GlassTreatmentAdded");
        LogAction("AddGlassTreatment", "");
    }

    private void BtnAddQualityClass_Click(object sender, RoutedEventArgs e)
    {
        AddLookupRow(_qualityClasses, GridQualityClasses, "NEW_CLASS", T("QASET.Default.NewQualityClass"));
        TxtStatus.Text = T("QASET.Status.QualityClassAdded");
        LogAction("AddQualityClass", "");
    }

    private static void AddLookupRow(
        ObservableCollection<EditableRow<QualityLookupItem>> collection,
        DataGrid grid,
        string code,
        string name)
    {
        var sortOrder = collection.Count == 0
            ? 10
            : collection.Max(row => row.Item.SortOrder) + 10;

        var row = new EditableRow<QualityLookupItem>(
            new QualityLookupItem
            {
                Code = code,
                Name = name,
                IsActive = true,
                SortOrder = sortOrder,
                Notes = string.Empty
            },
            EditableRowState.Added);

        collection.Add(row);
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
    }

    private void BtnDeleteCustomer_Click(object sender, RoutedEventArgs e)
    {
        var count = DeleteSelectedRows(GridCustomers, _customers);
        TxtStatus.Text = TF("QASET.Status.CustomersMarkedDeleted", count);
        LogAction("MarkCustomersDeleted", $"Count={count}");
    }

    private void BtnDeleteColorType_Click(object sender, RoutedEventArgs e)
    {
        var count = DeleteSelectedRows(GridColorTypes, _colorTypes);
        TxtStatus.Text = TF("QASET.Status.ColorTypesMarkedDeleted", count);
        LogAction("MarkColorTypesDeleted", $"Count={count}");
    }

    private void BtnDeleteGlassTreatment_Click(object sender, RoutedEventArgs e)
    {
        var count = DeleteSelectedRows(GridGlassTreatments, _glassTreatments);
        TxtStatus.Text = TF("QASET.Status.GlassTreatmentsMarkedDeleted", count);
        LogAction("MarkGlassTreatmentsDeleted", $"Count={count}");
    }

    private void BtnDeleteQualityClass_Click(object sender, RoutedEventArgs e)
    {
        var count = DeleteSelectedRows(GridQualityClasses, _qualityClasses);
        TxtStatus.Text = TF("QASET.Status.QualityClassesMarkedDeleted", count);
        LogAction("MarkQualityClassesDeleted", $"Count={count}");
    }

    private static int DeleteSelectedRows<T>(
        DataGrid grid,
        ObservableCollection<EditableRow<T>> collection)
    {
        var selectedRows = grid.SelectedItems
            .OfType<EditableRow<T>>()
            .ToList();

        foreach (var row in selectedRows)
        {
            if (row.State == EditableRowState.Added)
            {
                collection.Remove(row);
            }
            else
            {
                row.State = EditableRowState.Deleted;
            }
        }

        grid.Items.Refresh();
        return selectedRows.Count;
    }

    private void BtnSaveCustomers_Click(object sender, RoutedEventArgs e)
    {
        CommitGrid(GridCustomers);

        var items = _customers
            .Where(row => row.State != EditableRowState.Deleted)
            .Select(row => row.Item)
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name)
            .ToList();

        _repository.SaveCustomers(items);

        LogCustomerChanges();

        TxtStatus.Text = TF("QASET.Status.CustomersSaved", items.Count);
        LogAction("SaveCustomers", $"Count={items.Count}");

        LoadData();
    }

    private void BtnSaveColorTypes_Click(object sender, RoutedEventArgs e)
    {
        SaveLookupRows(GridColorTypes, _colorTypes, _originalColorTypes, _repository.SaveColorTypes, T("QASET.Tab.ColorTypes"), "QualityColorType", "SaveColorTypes");
    }

    private void BtnSaveGlassTreatments_Click(object sender, RoutedEventArgs e)
    {
        SaveLookupRows(GridGlassTreatments, _glassTreatments, _originalGlassTreatments, _repository.SaveGlassTreatments, T("QASET.Tab.GlassTreatments"), "QualityGlassTreatment", "SaveGlassTreatments");
    }

    private void BtnSaveQualityClasses_Click(object sender, RoutedEventArgs e)
    {
        SaveLookupRows(GridQualityClasses, _qualityClasses, _originalQualityClasses, _repository.SaveQualityClasses, T("QASET.Tab.QualityClasses"), "QualityClass", "SaveQualityClasses");
    }

    private void SaveLookupRows(
        DataGrid grid,
        ObservableCollection<EditableRow<QualityLookupItem>> collection,
        IReadOnlyList<QualityLookupItem> originalItems,
        Action<IEnumerable<QualityLookupItem>> saveAction,
        string displayName,
        string entityName,
        string logAction)
    {
        CommitGrid(grid);

        var items = collection
            .Where(row => row.State != EditableRowState.Deleted)
            .Select(row => row.Item)
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Code) ||
                !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToList();

        saveAction(items);

        LogLookupChanges(entityName, collection, originalItems);

        TxtStatus.Text = TF("QASET.Status.LookupSaved", displayName, items.Count);
        LogAction(logAction, $"Count={items.Count}");

        LoadData();
    }

    private void GridCustomers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        MarkRowModified(e.Row.Item);
    }

    private void GridLookup_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        MarkRowModified(e.Row.Item);
    }

    private static void MarkRowModified(object? rowObject)
    {
        switch (rowObject)
        {
            case EditableRow<QualityCustomer> { State: not EditableRowState.Added and not EditableRowState.Deleted } customerRow:
                customerRow.MarkModified();
                break;

            case EditableRow<QualityLookupItem> { State: not EditableRowState.Added and not EditableRowState.Deleted } lookupRow:
                lookupRow.MarkModified();
                break;
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("QASET.Dialog.ReloadTitle"),
            HasUnsavedChanges
                ? T("QASET.Dialog.ReloadUnsavedMessage")
                : T("QASET.Dialog.ReloadMessage"));

        if (!confirm)
        {
            return;
        }

        LogAction("ReloadQualitySettings", $"HadUnsavedChanges={HasUnsavedChanges}");
        LoadData();
    }

    private void EnsureDefaultLookupFiles()
    {
        if (!_repository.LoadColorTypes().Any())
        {
            _repository.SaveColorTypes(new[]
            {
                new QualityLookupItem { Code = "UV", Name = "UV", IsActive = true, SortOrder = 10 },
                new QualityLookupItem { Code = "UV_LED", Name = "UV LED", IsActive = true, SortOrder = 20 },
                new QualityLookupItem { Code = "KERAMIKA", Name = "Keramika", IsActive = true, SortOrder = 30 },
                new QualityLookupItem { Code = "PERBUSEAL", Name = "Perbuseal", IsActive = true, SortOrder = 40 },
                new QualityLookupItem { Code = "HORKA_RAZBA", Name = "Horká ražba", IsActive = true, SortOrder = 50 }
            });

            LogAction("CreateDefaultColorTypes", "");
        }

        if (!_repository.LoadGlassTreatments().Any())
        {
            _repository.SaveGlassTreatments(new[]
            {
                new QualityLookupItem { Code = "CIRE", Name = "Čiré", IsActive = true, SortOrder = 10 },
                new QualityLookupItem { Code = "STRIKANE", Name = "Stříkané", IsActive = true, SortOrder = 20 },
                new QualityLookupItem { Code = "OPAL", Name = "Opálové", IsActive = true, SortOrder = 30 },
                new QualityLookupItem { Code = "MAT", Name = "Matované", IsActive = true, SortOrder = 40 }
            });

            LogAction("CreateDefaultGlassTreatments", "");
        }

        if (!_repository.LoadQualityClasses().Any())
        {
            _repository.SaveQualityClasses(new[]
            {
                new QualityLookupItem { Code = "A", Name = "A", IsActive = true, SortOrder = 10 },
                new QualityLookupItem { Code = "B", Name = "B", IsActive = true, SortOrder = 20 },
                new QualityLookupItem { Code = "C", Name = "C", IsActive = true, SortOrder = 30 }
            });

            LogAction("CreateDefaultQualityClasses", "");
        }
    }


    private void LogCustomerChanges()
    {
        foreach (var row in _customers)
        {
            var customer = row.Item;
            var key = BuildCustomerKey(customer);

            if (row.State == EditableRowState.Deleted)
            {
                _logger?.AuditDeleted(
                    "QASET",
                    "QualityCustomer",
                    key,
                    _currentUserName,
                    BuildCustomerDetail(customer));

                continue;
            }

            var original = FindOriginalCustomer(customer);

            if (row.State == EditableRowState.Added || original is null)
            {
                _logger?.AuditCreated(
                    "QASET",
                    "QualityCustomer",
                    key,
                    _currentUserName,
                    BuildCustomerDetail(customer));

                continue;
            }

            LogAuditFieldChange("QualityCustomer", key, "Code", original.Code, customer.Code);
            LogAuditFieldChange("QualityCustomer", key, "Name", original.Name, customer.Name);
            LogAuditFieldChange("QualityCustomer", key, "SourceId", original.SourceId.ToString(), customer.SourceId.ToString());
            LogAuditFieldChange("QualityCustomer", key, "IsActive", original.IsActive.ToString(), customer.IsActive.ToString());
            LogAuditFieldChange("QualityCustomer", key, "IsLoreal", original.IsLoreal.ToString(), customer.IsLoreal.ToString());
        }
    }

    private void LogLookupChanges(
        string entityName,
        ObservableCollection<EditableRow<QualityLookupItem>> rows,
        IReadOnlyList<QualityLookupItem> originalItems)
    {
        foreach (var row in rows)
        {
            var item = row.Item;
            var key = BuildLookupKey(item);

            if (row.State == EditableRowState.Deleted)
            {
                _logger?.AuditDeleted(
                    "QASET",
                    entityName,
                    key,
                    _currentUserName,
                    BuildLookupDetail(item));

                continue;
            }

            var original = FindOriginalLookup(item, originalItems);

            if (row.State == EditableRowState.Added || original is null)
            {
                _logger?.AuditCreated(
                    "QASET",
                    entityName,
                    key,
                    _currentUserName,
                    BuildLookupDetail(item));

                continue;
            }

            LogAuditFieldChange(entityName, key, "Code", original.Code, item.Code);
            LogAuditFieldChange(entityName, key, "Name", original.Name, item.Name);
            LogAuditFieldChange(entityName, key, "SortOrder", original.SortOrder.ToString(), item.SortOrder.ToString());
            LogAuditFieldChange(entityName, key, "IsActive", original.IsActive.ToString(), item.IsActive.ToString());
            LogAuditFieldChange(entityName, key, "Notes", original.Notes, item.Notes);
        }
    }

    private void LogAuditFieldChange(
        string entityName,
        string entityId,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "QASET",
            entityName,
            entityId,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private QualityCustomer? FindOriginalCustomer(QualityCustomer current)
    {
        if (!string.IsNullOrWhiteSpace(current.Code))
        {
            var byCode = _originalCustomers.FirstOrDefault(item =>
                string.Equals(item.Code, current.Code, StringComparison.OrdinalIgnoreCase));

            if (byCode is not null)
            {
                return byCode;
            }
        }

        if (current.SourceId > 0)
        {
            var bySourceId = _originalCustomers.FirstOrDefault(item => item.SourceId == current.SourceId);

            if (bySourceId is not null)
            {
                return bySourceId;
            }
        }

        return _originalCustomers.FirstOrDefault(item =>
            string.Equals(item.Name, current.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static QualityLookupItem? FindOriginalLookup(
        QualityLookupItem current,
        IReadOnlyList<QualityLookupItem> originalItems)
    {
        if (!string.IsNullOrWhiteSpace(current.Code))
        {
            var byCode = originalItems.FirstOrDefault(item =>
                string.Equals(item.Code, current.Code, StringComparison.OrdinalIgnoreCase));

            if (byCode is not null)
            {
                return byCode;
            }
        }

        return originalItems.FirstOrDefault(item =>
            string.Equals(item.Name, current.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCustomerKey(QualityCustomer customer)
    {
        if (!string.IsNullOrWhiteSpace(customer.Code))
        {
            return customer.Code.Trim();
        }

        if (customer.SourceId > 0)
        {
            return customer.SourceId.ToString();
        }

        return string.IsNullOrWhiteSpace(customer.Name)
            ? "UNKNOWN_CUSTOMER"
            : customer.Name.Trim();
    }

    private static string BuildLookupKey(QualityLookupItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Code))
        {
            return item.Code.Trim();
        }

        return string.IsNullOrWhiteSpace(item.Name)
            ? "UNKNOWN_LOOKUP"
            : item.Name.Trim();
    }

    private static string BuildCustomerDetail(QualityCustomer customer)
    {
        return $"Code={customer.Code}; Name={customer.Name}; SourceId={customer.SourceId}; IsActive={customer.IsActive}; IsLoreal={customer.IsLoreal}";
    }

    private static string BuildLookupDetail(QualityLookupItem item)
    {
        return $"Code={item.Code}; Name={item.Name}; SortOrder={item.SortOrder}; IsActive={item.IsActive}; Notes={item.Notes}";
    }

    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static bool IsChanged<T>(EditableRow<T> row)
    {
        return row.State != EditableRowState.Unchanged;
    }

    private void LogAction(string action, string details)
    {
        _logger?.AdminAction(
            "QASET",
            action,
            _currentUserName,
            details);
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;

        if (!IsMissing(value, key))
        {
            return value;
        }

        return FallbackTranslations.TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    private string TF(string key, params object[] args)
    {
        try
        {
            if (_translateFormat is not null)
            {
                var translated = _translateFormat(key, args);

                if (!IsMissing(translated, key))
                {
                    return translated;
                }
            }

            return string.Format(T(key), args);
        }
        catch
        {
            return T(key);
        }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private static QualityCustomer CloneCustomer(QualityCustomer source)
    {
        return new QualityCustomer
        {
            Code = source.Code,
            Name = source.Name,
            IsActive = source.IsActive,
            IsLoreal = source.IsLoreal,
            SourceId = source.SourceId
        };
    }

    private static QualityLookupItem CloneLookup(QualityLookupItem source)
    {
        return new QualityLookupItem
        {
            Code = source.Code,
            Name = source.Name,
            IsActive = source.IsActive,
            SortOrder = source.SortOrder,
            Notes = source.Notes
        };
    }

    private static readonly Dictionary<string, string> FallbackTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QASET.Title"] = "QASET - Quality settings",
        ["QASET.Subtitle"] = "Manage quality master data and lookup values. New rows are green, modified rows are orange and rows marked for deletion are red.",
        ["QASET.Tab.Customers"] = "Customers",
        ["QASET.Tab.ColorTypes"] = "Color types",
        ["QASET.Tab.GlassTreatments"] = "Glass treatments",
        ["QASET.Tab.QualityClasses"] = "Quality classes",
        ["QASET.Tab.Paths"] = "Paths",
        ["QASET.Button.Add"] = "Add",
        ["QASET.Button.Delete"] = "Delete",
        ["QASET.Button.Save"] = "Save",
        ["QASET.Button.Reload"] = "Reload",
        ["QASET.Column.Code"] = "Code",
        ["QASET.Column.Name"] = "Name",
        ["QASET.Column.Active"] = "Active",
        ["QASET.Column.Loreal"] = "L'Oréal",
        ["QASET.Column.SourceId"] = "Source ID",
        ["QASET.Column.JsonCode"] = "JSON Code",
        ["QASET.Column.SortOrder"] = "Sort order",
        ["QASET.Column.Notes"] = "Notes",
        ["QASET.Paths.BasePath"] = "Quality base path",
        ["QASET.Paths.QualityPath"] = "Quality data path",
        ["QASET.Paths.Customers"] = "Customers",
        ["QASET.Paths.ColorTypes"] = "Color types",
        ["QASET.Paths.GlassTreatments"] = "Glass treatments",
        ["QASET.Paths.QualityClasses"] = "Quality classes",
        ["QASET.Status.Ready"] = "QASET ready. Customers: {0:N0}, color types: {1:N0}, glass treatments: {2:N0}, quality classes: {3:N0}",
        ["QASET.Status.CustomerAdded"] = "New customer added.",
        ["QASET.Status.ColorTypeAdded"] = "New color type added.",
        ["QASET.Status.GlassTreatmentAdded"] = "New glass treatment added.",
        ["QASET.Status.QualityClassAdded"] = "New quality class added.",
        ["QASET.Status.CustomersMarkedDeleted"] = "Customer rows marked for deletion: {0:N0}",
        ["QASET.Status.ColorTypesMarkedDeleted"] = "Color type rows marked for deletion: {0:N0}",
        ["QASET.Status.GlassTreatmentsMarkedDeleted"] = "Glass treatment rows marked for deletion: {0:N0}",
        ["QASET.Status.QualityClassesMarkedDeleted"] = "Quality class rows marked for deletion: {0:N0}",
        ["QASET.Status.CustomersSaved"] = "Customers saved. Count: {0:N0}",
        ["QASET.Status.LookupSaved"] = "{0} saved. Count: {1:N0}",
        ["QASET.Default.NewCustomer"] = "New customer",
        ["QASET.Default.NewColorType"] = "New color type",
        ["QASET.Default.NewGlassTreatment"] = "New glass treatment",
        ["QASET.Default.NewQualityClass"] = "New quality class",
        ["QASET.Dialog.UnsavedTitle"] = "QASET - unsaved changes",
        ["QASET.Dialog.UnsavedMessage"] = "Quality settings contain unsaved changes. Do you really want to continue without saving?",
        ["QASET.Dialog.ReloadTitle"] = "QASET - reload data",
        ["QASET.Dialog.ReloadMessage"] = "Reload data from JSON files?",
        ["QASET.Dialog.ReloadUnsavedMessage"] = "Reload data from JSON files? Unsaved changes will be discarded."
    };
}
