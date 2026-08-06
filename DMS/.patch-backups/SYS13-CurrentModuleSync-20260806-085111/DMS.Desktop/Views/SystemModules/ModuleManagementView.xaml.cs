using DMS.Core.Transactions;
using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemModules;

public partial class ModuleManagementView : UserControl, IUnsavedChangesGuard
{
    private const string Area = "SYS13";

    private readonly DmsModuleManagementService _service;
    private readonly ObservableCollection<DmsModuleDefinition> _modules = new();
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly string? _transactionsPath;
    private readonly Action? _afterSave;
    private readonly Func<string, string> _translate;
    private readonly Func<string, object[], string> _translateFormat;

    private ICollectionView? _view;
    private bool _isLoading;
    private List<DmsModuleDefinition> _originalModules = new();

    public bool HasUnsavedChanges => _modules.Any(x => x.State != "Unchanged");

    public ModuleManagementView(string modulesPath)
        : this(
            modulesPath,
            transactionsPath: null,
            logger: null,
            currentUserName: null,
            afterSave: null,
            translate: null,
            translateFormat: null)
    {
    }

    public ModuleManagementView(
        string modulesPath,
        DmsLogger? logger = null,
        string? currentUserName = null)
        : this(
            modulesPath,
            transactionsPath: null,
            logger: logger,
            currentUserName: currentUserName,
            afterSave: null,
            translate: null,
            translateFormat: null)
    {
    }

    public ModuleManagementView(
        string modulesPath,
        string? transactionsPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Action? afterSave = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;

        _transactionsPath = transactionsPath;
        _afterSave = afterSave;
        _translate = translate ?? DefaultTranslate;
        _translateFormat = translateFormat ?? DefaultTranslateFormat;

        Sys13ModuleTextConverter.Translate = T;

        _service = new DmsModuleManagementService(modulesPath);

        _modules.CollectionChanged += Modules_CollectionChanged;
        GridModules.ItemsSource = _modules;

        ApplyLocalization();
        LoadModules();

        _logger?.AdminAction(
            Area,
            "OpenModuleManagement",
            _currentUserName,
            $"ModulesPath={modulesPath}; TransactionsPath={_transactionsPath ?? string.Empty}");
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("SYS13.Dialog.UnsavedTitle"),
            T("SYS13.Dialog.UnsavedMessage"));
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SYS13.Title");
        TxtCardTitle.Text = T("SYS13.CardTitle");
        TxtTechnicalNote.Text = T("SYS13.TechnicalNote");
        TxtFilterLabel.Text = T("SYS13.FilterLabel");
        ChkShowInactive.Content = T("SYS13.ShowInactive");

        ColCode.Header = T("SYS13.Column.Code");
        ColName.Header = T("SYS13.Column.Name");
        ColDescription.Header = T("SYS13.Column.Description");
        ColSortOrder.Header = T("SYS13.Column.SortOrder");
        ColIsActive.Header = T("SYS13.Column.IsActive");

        BtnReload.Content = T("SYS13.Button.Reload");
        BtnSave.Content = T("SYS13.Button.Save");
        BtnMarkDeleted.Content = T("SYS13.Button.MarkDeleted");
        BtnRestoreRow.Content = T("SYS13.Button.RestoreRow");
    }

    private void Modules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoading || e.NewItems is null)
        {
            return;
        }

        foreach (var module in e.NewItems.OfType<DmsModuleDefinition>())
        {
            module.MarkAdded();
        }
    }

    private void LoadModules()
    {
        _isLoading = true;

        try
        {
            _modules.Clear();

            foreach (var module in _service.LoadAll())
            {
                module.MarkUnchanged();
                _modules.Add(module);
            }

            _originalModules = _modules
                .Select(CloneModule)
                .ToList();

            _view = CollectionViewSource.GetDefaultView(_modules);
            _view.Filter = FilterModule;

            TxtStatus.Text = T("SYS13.Status.Loaded", _modules.Count);

            _logger?.AdminAction(
                Area,
                "LoadModules",
                _currentUserName,
                $"Count={_modules.Count}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool FilterModule(object item)
    {
        if (item is not DmsModuleDefinition module)
        {
            return false;
        }

        if (ChkShowInactive.IsChecked != true && !module.IsActive)
        {
            return false;
        }

        var filter = TxtFilter.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(module.Code, filter)
               || Contains(module.Name, filter)
               || Contains(module.Description, filter)
               || Contains(module.SortOrder.ToString(), filter)
               || Contains(module.State, filter)
               || Contains(DmsModuleText.Name(module, T), filter)
               || Contains(DmsModuleText.Description(module, T), filter);
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _view?.Refresh();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        if (HasUnsavedChanges)
        {
            var confirmUnsaved = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS13.Dialog.ReloadTitle"),
                T("SYS13.Dialog.ReloadUnsavedMessage"));

            if (!confirmUnsaved)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS13.Dialog.ReloadTitle"),
                T("SYS13.Dialog.ReloadMessage"));

            if (!confirm)
            {
                return;
            }
        }

        _logger?.AdminAction(
            Area,
            "ReloadModules",
            _currentUserName,
            $"HadUnsavedChanges={HasUnsavedChanges}");

        LoadModules();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        var validationMessage = ValidateModules();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _logger?.AdminAction(
                Area,
                "ValidationFailed",
                _currentUserName,
                "Module validation failed.");

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS13.Dialog.ValidationTitle"),
                validationMessage);

            return;
        }

        var usageWarning = BuildInactiveOrDeletedModuleUsageWarning();

        if (!string.IsNullOrWhiteSpace(usageWarning))
        {
            var confirmUsage = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS13.Dialog.UsageWarningTitle"),
                usageWarning);

            if (!confirmUsage)
            {
                return;
            }
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("SYS13.Dialog.SaveTitle"),
            T("SYS13.Dialog.SaveMessage"));

        if (!confirm)
        {
            return;
        }

        try
        {
            LogModuleChanges();

            var savedCount = _modules.Count(x => x.State != "Deleted");
            var deletedCount = _modules.Count(x => x.State == "Deleted");
            var addedCount = _modules.Count(x => x.State == "Added");
            var modifiedCount = _modules.Count(x => x.State == "Modified");

            _service.SaveAll(_modules.Where(x => x.State != "Deleted"));

            _logger?.AdminAction(
                Area,
                "SaveModules",
                _currentUserName,
                $"Saved={savedCount}; Added={addedCount}; Modified={modifiedCount}; Deleted={deletedCount}");

            TxtStatus.Text = T("SYS13.Status.Saved", savedCount, DateTime.Now);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS13.Dialog.SavedTitle"),
                T("SYS13.Dialog.SavedMessage"));

            _afterSave?.Invoke();
            LoadModules();
        }
        catch (Exception ex)
        {
            _logger?.Error("SYS13 module save failed.", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS13.Dialog.ErrorTitle"),
                T("SYS13.Dialog.SaveFailed", ex.Message));
        }
    }

    private void BtnMarkDeleted_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        if (GridModules.SelectedItem is not DmsModuleDefinition module)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS13.Dialog.DeleteTitle"),
                T("SYS13.Dialog.SelectModuleToDelete"));

            return;
        }

        var usage = GetModuleUsage(module).ToList();

        if (usage.Count > 0)
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS13.Dialog.UsageWarningTitle"),
                T("SYS13.Dialog.DeleteUsedModuleMessage", module.Code, string.Join(", ", usage.Take(12))));

            if (!confirm)
            {
                return;
            }
        }

        module.MarkDeleted();
        _view?.Refresh();

        _logger?.AdminAction(
            Area,
            "MarkModuleDeleted",
            _currentUserName,
            $"Module={module.Code}; Name={module.Name}");

        TxtStatus.Text = T("SYS13.Status.MarkedDeleted", module.Code);
    }

    private void BtnRestoreRow_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        if (GridModules.SelectedItem is not DmsModuleDefinition module)
        {
            return;
        }

        if (module.State == "Added")
        {
            _modules.Remove(module);
            TxtStatus.Text = T("SYS13.Status.AddedRowRemoved");
            return;
        }

        RestoreModuleState(module);

        _view?.Refresh();
        TxtStatus.Text = T("SYS13.Status.Restored", module.Code);
    }

    private void RestoreModuleState(DmsModuleDefinition module)
    {
        var original = _originalModules.FirstOrDefault(x =>
            string.Equals(x.Code, module.Code, StringComparison.OrdinalIgnoreCase));

        if (original is null)
        {
            module.MarkModified();
            return;
        }

        if (ModuleEquals(original, module))
        {
            module.MarkUnchanged();
        }
        else
        {
            module.MarkModified();
        }
    }

    private string? ValidateModules()
    {
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in _modules.Where(x => x.State != "Deleted"))
        {
            if (string.IsNullOrWhiteSpace(module.Code) &&
                string.IsNullOrWhiteSpace(module.Name) &&
                string.IsNullOrWhiteSpace(module.Description))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(module.Code))
            {
                return T("SYS13.Validation.EmptyCode");
            }

            if (module.Code.Any(char.IsWhiteSpace))
            {
                return T("SYS13.Validation.CodeHasSpaces", module.Code);
            }

            if (!usedCodes.Add(module.Code.Trim()))
            {
                return T("SYS13.Validation.DuplicateCode", module.Code);
            }

            if (string.IsNullOrWhiteSpace(module.Name))
            {
                return T("SYS13.Validation.EmptyName", module.Code);
            }

            if (!usedNames.Add(module.Name.Trim()))
            {
                return T("SYS13.Validation.DuplicateName", module.Name);
            }

            if (module.SortOrder < 0)
            {
                return T("SYS13.Validation.NegativeSortOrder", module.Code);
            }
        }

        return null;
    }

    private string BuildInactiveOrDeletedModuleUsageWarning()
    {
        var warnings = new List<string>();

        foreach (var module in _modules.Where(x => x.State == "Deleted" || !x.IsActive))
        {
            var usage = GetModuleUsage(module).ToList();

            if (usage.Count == 0)
            {
                continue;
            }

            warnings.Add(T(
                "SYS13.Validation.ModuleUsedByTransactionsLine",
                module.Code,
                module.Name,
                string.Join(", ", usage.Take(12))));
        }

        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        return T("SYS13.Validation.ModuleUsageWarningIntro")
               + Environment.NewLine
               + Environment.NewLine
               + string.Join(Environment.NewLine, warnings)
               + Environment.NewLine
               + Environment.NewLine
               + T("SYS13.Validation.ModuleUsageWarningContinue");
    }

    private IEnumerable<string> GetModuleUsage(DmsModuleDefinition module)
    {
        if (string.IsNullOrWhiteSpace(_transactionsPath))
        {
            return Enumerable.Empty<string>();
        }

        try
        {
            var original = _originalModules.FirstOrDefault(x =>
                string.Equals(x.Code, module.Code, StringComparison.OrdinalIgnoreCase));

            var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(module.Name))
            {
                moduleNames.Add(module.Name.Trim());
            }

            if (!string.IsNullOrWhiteSpace(original?.Name))
            {
                moduleNames.Add(original.Name.Trim());
            }

            if (moduleNames.Count == 0)
            {
                return Enumerable.Empty<string>();
            }

            var loader = new TransactionDefinitionLoader();
            var transactions = loader.LoadFromJson(_transactionsPath);

            return transactions
                .Where(transaction => moduleNames.Contains(transaction.Module))
                .OrderBy(transaction => transaction.Code)
                .Select(transaction => transaction.Code)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.Error("SYS13 module usage check failed.", ex);
            return Enumerable.Empty<string>();
        }
    }

    private void LogModuleChanges()
    {
        foreach (var module in _modules)
        {
            var original = _originalModules.FirstOrDefault(x =>
                string.Equals(x.Code, module.Code, StringComparison.OrdinalIgnoreCase));

            if (module.State == "Deleted")
            {
                _logger?.AuditDeleted(
                    Area,
                    "Module",
                    module.Code,
                    _currentUserName,
                    $"Name={module.Name}; Description={module.Description}; SortOrder={module.SortOrder}; IsActive={module.IsActive}");

                continue;
            }

            if (original is null)
            {
                _logger?.AuditCreated(
                    Area,
                    "Module",
                    module.Code,
                    _currentUserName,
                    $"Name={module.Name}; Description={module.Description}; SortOrder={module.SortOrder}; IsActive={module.IsActive}");

                continue;
            }

            LogModuleFieldChange(module.Code, "Name", original.Name, module.Name);
            LogModuleFieldChange(module.Code, "Description", original.Description, module.Description);
            LogModuleFieldChange(module.Code, "SortOrder", original.SortOrder.ToString(), module.SortOrder.ToString());
            LogModuleFieldChange(module.Code, "IsActive", original.IsActive.ToString(), module.IsActive.ToString());
        }
    }

    private void LogModuleFieldChange(
        string moduleCode,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            Area,
            "Module",
            moduleCode,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private void CommitGridEdit()
    {
        GridModules.CommitEdit(DataGridEditingUnit.Cell, true);
        GridModules.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private string T(string key)
    {
        return _translate(key);
    }

    private string T(string key, params object[] args)
    {
        return _translateFormat(key, args);
    }

    private static string DefaultTranslate(string key)
    {
        return key;
    }

    private static string DefaultTranslateFormat(string key, params object[] args)
    {
        return args.Length == 0
            ? key
            : string.Format(key, args);
    }

    private static DmsModuleDefinition CloneModule(DmsModuleDefinition source)
    {
        var clone = new DmsModuleDefinition
        {
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            SortOrder = source.SortOrder,
            IsActive = source.IsActive
        };

        clone.MarkUnchanged();
        return clone;
    }

    private static bool ModuleEquals(
        DmsModuleDefinition a,
        DmsModuleDefinition b)
    {
        return string.Equals(a.Code, b.Code, StringComparison.Ordinal)
               && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
               && string.Equals(a.Description, b.Description, StringComparison.Ordinal)
               && a.SortOrder == b.SortOrder
               && a.IsActive == b.IsActive;
    }
}
