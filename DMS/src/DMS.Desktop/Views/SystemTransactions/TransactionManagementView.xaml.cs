using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.Configuration.Transactions;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemTransactions;

public partial class TransactionManagementView : UserControl, IUnsavedChangesGuard
{
    private readonly string _rolesPath;
    private readonly string _modulesPath;
    private readonly ObservableCollection<Sys11ModuleSelectionItem> _availableModules = new();
    public ObservableCollection<Sys11ModuleSelectionItem> AvailableModules => _availableModules;
    private readonly TransactionManagementService _service;
    private readonly Action? _afterSave;
    private readonly ObservableCollection<TransactionEditorItem> _transactions = new();
    private ICollectionView? _view;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName = "UNKNOWN";
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private List<TransactionEditorItem> _originalTransactions = new();

    public bool HasUnsavedChanges => _transactions.Any(x => x.State != "Unchanged");

    public TransactionManagementView(
        string transactionsPath,
        string rolesPath,
        string modulesPath,
        Action? afterSave = null)
        : this(
            transactionsPath,
            rolesPath,
            modulesPath,
            logger: null,
            currentUserName: null,
            afterSave: afterSave,
            translate: null,
            translateFormat: null)
    {
    }

    public TransactionManagementView(
        string transactionsPath,
        string rolesPath,
        string modulesPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Action? afterSave = null)
        : this(
            transactionsPath,
            rolesPath,
            modulesPath,
            logger,
            currentUserName,
            afterSave,
            translate: null,
            translateFormat: null)
    {
    }

    public TransactionManagementView(
        string transactionsPath,
        string rolesPath,
        string modulesPath,
        DmsLogger? logger,
        string? currentUserName,
        Action? afterSave,
        Func<string, string>? translate,
        Func<string, object[], string>? translateFormat)
    {
        InitializeComponent();

        _rolesPath = rolesPath;
        _modulesPath = modulesPath;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        if (Resources["Sys11TransactionTextConverter"] is Sys11TransactionTextConverter converter)
        {
            converter.Translate = key => _translate?.Invoke(key) ?? key;
        }

        _service = new TransactionManagementService(transactionsPath);
        _afterSave = afterSave;

        GridTransactions.ItemsSource = _transactions;

        ApplyLocalization();
        LoadModules();
        LoadTransactions();

        _logger?.AdminAction(
            "SYS11",
            "OpenTransactionManagement",
            _currentUserName,
            $"Transactions={_transactions.Count}; RolesPath={_rolesPath}; ModulesPath={_modulesPath}");
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("SYS11.UnsavedChangesTitle", "SYS11 - Unsaved changes"),
            T(
                "SYS11.UnsavedChangesMessage",
                "Transaction management contains unsaved changes.\n\nDo you really want to continue without saving?"));
    }

    private void ApplyLocalization()
    {
        SetGridColumnHeaders();
        TrySetTextBlock("TxtTitle", T("SYS11.Title", "SYS11 - Transaction management"));
        TrySetTextBlock("TxtSubtitle", T("SYS11.Subtitle", "Manage transaction definitions stored in transactions.json."));
        TrySetTextBlock("TxtTechnicalNote", T("SYS11.TechnicalNote", "Code and HandlerKey are read-only on purpose. Changing them can break dispatcher and handler mapping."));
        TrySetTextBlock("TxtFilterLabel", T("SYS11.Filter", "Filter:"));
        TrySetContent("ChkShowInactive", T("SYS11.ShowInactive", "Show inactive"));
        TrySetContent("BtnReload", T("SYS11.Reload", "Reload"));
        TrySetContent("BtnSave", T("SYS11.Save", "Save transactions"));
        TrySetContent("BtnEditRoles", T("SYS11.EditRoles", "Edit roles"));
    }

    private void SetGridColumnHeaders()
    {
        foreach (var column in GridTransactions.Columns)
        {
            var bindingPath = GetColumnBindingPath(column);
            var header = bindingPath switch
            {
                nameof(TransactionEditorItem.Code) => T("SYS11.Column.Code", "Code"),
                nameof(TransactionEditorItem.Name) => T("SYS11.Column.Name", "Name"),
                nameof(TransactionEditorItem.Module) => T("SYS11.Column.Module", "Module"),
                nameof(TransactionEditorItem.Description) => T("SYS11.Column.Description", "Description"),
                nameof(TransactionEditorItem.HandlerKey) => T("SYS11.Column.HandlerKey", "Handler key"),
                nameof(TransactionEditorItem.RequiresArticleNumber) => T("SYS11.Column.RequiresArticleNumber", "Requires SAP ID"),
                nameof(TransactionEditorItem.IsActive) => T("SYS11.Column.IsActive", "Active"),
                nameof(TransactionEditorItem.RolesText) => T("SYS11.Column.Roles", "Roles"),
                nameof(TransactionEditorItem.State) => T("SYS11.Column.State", "State"),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(header))
            {
                column.Header = header;
            }
        }
    }

    private static string? GetColumnBindingPath(DataGridColumn column)
    {
        if (column.SortMemberPath is { Length: > 0 } sortMemberPath)
        {
            return sortMemberPath;
        }

        if (column is DataGridBoundColumn boundColumn &&
            boundColumn.Binding is Binding binding)
        {
            return binding.Path?.Path;
        }

        return null;
    }

    private void TrySetTextBlock(string name, string value)
    {
        if (FindName(name) is TextBlock textBlock)
        {
            textBlock.Text = value;
        }
    }

    private void TrySetContent(string name, string value)
    {
        if (FindName(name) is ContentControl contentControl)
        {
            contentControl.Content = value;
        }
    }

    private void LoadTransactions()
    {
        _transactions.Clear();

        foreach (var item in _service.LoadAll())
        {
            item.MarkUnchanged();
            _transactions.Add(item);
        }

        _view = CollectionViewSource.GetDefaultView(_transactions);
        _view.Filter = FilterTransaction;

        _originalTransactions = _transactions
            .Select(CloneTransaction)
            .ToList();

        TxtStatus.Text = T("SYS11.Status.Loaded", "Loaded transactions: {0}", _transactions.Count);
    }

    private void LoadModules()
    {
        _availableModules.Clear();

        var moduleService = new DmsModuleManagementService(_modulesPath);

        var modules = moduleService.LoadAll()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => LocalizeModuleName(x.Name))
            .ToList();

        foreach (var module in modules)
        {
            _availableModules.Add(new Sys11ModuleSelectionItem
            {
                RawName = module.Name,
                DisplayName = LocalizeModuleName(module.Name)
            });
        }
    }

    private bool FilterTransaction(object item)
    {
        if (item is not TransactionEditorItem transaction)
        {
            return false;
        }

        if (ChkShowInactive.IsChecked != true && !transaction.IsActive)
        {
            return false;
        }

        var filter = TxtFilter.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(transaction.Code, filter)
               || Contains(transaction.Name, filter)
               || Contains(LocalizeTransactionName(transaction), filter)
               || Contains(transaction.Module, filter)
               || Contains(LocalizeModuleName(transaction.Module), filter)
               || Contains(transaction.Description, filter)
               || Contains(LocalizeTransactionDescription(transaction), filter)
               || Contains(transaction.HandlerKey, filter)
               || Contains(transaction.RolesText, filter)
               || Contains(transaction.State, filter);
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
        CommitGridEdits();

        if (HasUnsavedChanges)
        {
            var confirmUnsaved = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS11.ReloadTitle", "SYS11 - Reload"),
                T(
                    "SYS11.ReloadUnsavedQuestion",
                    "The table contains unsaved changes.\n\nDo you want to discard them and reload transactions.json?"));

            if (!confirmUnsaved)
            {
                _logger?.AdminAction(
                    "SYS11",
                    "ReloadTransactionsCancelled",
                    _currentUserName,
                    "Reason=UnsavedChanges");
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS11.ReloadTitle", "SYS11 - Reload"),
                T("SYS11.ReloadQuestion", "Do you want to reload transactions.json?"));

            if (!confirm)
            {
                _logger?.AdminAction(
                    "SYS11",
                    "ReloadTransactionsCancelled",
                    _currentUserName,
                    "Reason=UserCancelled");
                return;
            }
        }

        LoadModules();
        LoadTransactions();

        _logger?.AdminAction(
            "SYS11",
            "ReloadTransactions",
            _currentUserName,
            $"Transactions={_transactions.Count}");
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();

        var validationMessage = ValidateTransactions();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _logger?.AdminAction(
                "SYS11",
                "SaveTransactionsFailed",
                _currentUserName,
                $"Reason=Validation; Message={validationMessage}");

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS11.ValidationTitle", "SYS11 - Transaction validation"),
                validationMessage);

            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("SYS11.SaveTitle", "SYS11 - Save"),
            T(
                "SYS11.SaveQuestion",
                "Do you want to save changes to transactions.json?\n\nAfter saving, the transaction list and left menu will be refreshed."));

        if (!confirm)
        {
            _logger?.AdminAction(
                "SYS11",
                "SaveTransactionsCancelled",
                _currentUserName,
                "Reason=UserCancelled");
            return;
        }

        try
        {
            LogTransactionChanges();

            var savedCount = _transactions.Count(x => x.State != "Deleted");
            var deletedCount = _transactions.Count(x => x.State == "Deleted");

            _service.SaveAll(_transactions.Where(x => x.State != "Deleted"));

            foreach (var transaction in _transactions)
            {
                transaction.MarkUnchanged();
            }

            _afterSave?.Invoke();

            TxtStatus.Text = T(
                "SYS11.Status.Saved",
                "Saved transactions: {0} | {1}",
                savedCount,
                DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));

            _logger?.AdminAction(
                "SYS11",
                "SaveTransactions",
                _currentUserName,
                $"Saved={savedCount}; Deleted={deletedCount}");

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS11.SavedTitle", "SYS11"),
                T("SYS11.SavedMessage", "Transactions have been saved."));

            LoadModules();
            LoadTransactions();
        }
        catch (Exception ex)
        {
            _logger?.Error(
                $"SYS11 SaveTransactions failed: {ex.Message}",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS11.ErrorTitle", "SYS11 - Error"),
                T("SYS11.SaveFailed", "Saving transactions failed:\n\n{0}", ex.Message));
        }
    }

    private void BtnEditRoles_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();

        if (GridTransactions.SelectedItem is not TransactionEditorItem transaction)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS11.RolesTitle", "SYS11 - Transaction roles"),
                T("SYS11.SelectTransactionForRoles", "Select the transaction whose roles you want to edit."));

            return;
        }

        if (transaction.State == "Deleted")
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS11.RolesTitle", "SYS11 - Transaction roles"),
                T("SYS11.RolesDeletedTransactionMessage", "Roles cannot be edited for a transaction marked for deletion."));

            return;
        }

        var roleService = new DmsRoleManagementService(_rolesPath);

        var roles = roleService.LoadAll()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .ToList();

        if (roles.Count == 0)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS11.RoleSelectionTitle", "SYS11 - Role selection"),
                T("SYS11.NoActiveRolesMessage", "There is no active role available. Check Config\\dms-roles.json or SYS12 role management."));

            return;
        }

        var dialog = new RoleSelectionWindow(
            roles,
            transaction.Roles,
            key => _translate?.Invoke(key) ?? key)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            transaction.Roles = dialog.SelectedRoleCodes;
            transaction.MarkModified();

            _logger?.AdminAction(
                "SYS11",
                "EditTransactionRoles",
                _currentUserName,
                $"Transaction={transaction.Code}; Roles={transaction.RolesText}");

            _view?.Refresh();

            TxtStatus.Text = T(
                "SYS11.Status.RolesUpdated",
                "Roles updated for transaction {0}. Do not forget to save changes.",
                transaction.Code);
        }
    }

    private string? ValidateTransactions()
    {
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in _transactions.Where(x => x.State != "Deleted"))
        {
            if (string.IsNullOrWhiteSpace(transaction.Code))
            {
                return T("SYS11.Validation.CodeRequired", "Transaction code must not be empty.");
            }

            if (!usedCodes.Add(transaction.Code.Trim()))
            {
                return T("SYS11.Validation.DuplicateCode", "Duplicate transaction code: {0}", transaction.Code);
            }

            if (string.IsNullOrWhiteSpace(transaction.Name))
            {
                return T("SYS11.Validation.NameRequired", "Transaction {0} must have a name.", transaction.Code);
            }

            if (string.IsNullOrWhiteSpace(transaction.Module))
            {
                return T("SYS11.Validation.ModuleRequired", "Transaction {0} must have a module.", transaction.Code);
            }

            if (string.IsNullOrWhiteSpace(transaction.HandlerKey))
            {
                return T("SYS11.Validation.HandlerKeyRequired", "Transaction {0} must have a HandlerKey.", transaction.Code);
            }

            if (transaction.Roles.Any(string.IsNullOrWhiteSpace))
            {
                return T("SYS11.Validation.EmptyRole", "Transaction {0} contains an empty role.", transaction.Code);
            }
        }

        return null;
    }

    private static TransactionEditorItem CloneTransaction(TransactionEditorItem source)
    {
        var clone = new TransactionEditorItem
        {
            Code = source.Code,
            Name = source.Name,
            Module = source.Module,
            Description = source.Description,
            HandlerKey = source.HandlerKey,
            RequiresArticleNumber = source.RequiresArticleNumber,
            IsActive = source.IsActive,
            Roles = source.Roles.ToList()
        };

        clone.MarkUnchanged();
        return clone;
    }

    private void LogTransactionChanges()
    {
        foreach (var transaction in _transactions)
        {
            var original = _originalTransactions.FirstOrDefault(x =>
                string.Equals(x.Code, transaction.Code, StringComparison.OrdinalIgnoreCase));

            if (transaction.State == "Deleted")
            {
                _logger?.AuditDeleted(
                    "SYS11",
                    "Transaction",
                    transaction.Code,
                    _currentUserName,
                    $"Name={transaction.Name}; Module={transaction.Module}; HandlerKey={transaction.HandlerKey}");

                continue;
            }

            if (original is null)
            {
                _logger?.AuditCreated(
                    "SYS11",
                    "Transaction",
                    transaction.Code,
                    _currentUserName,
                    $"Name={transaction.Name}; Module={transaction.Module}; HandlerKey={transaction.HandlerKey}; Roles={transaction.RolesText}");

                continue;
            }

            LogFieldChange(transaction.Code, "Name", original.Name, transaction.Name);
            LogFieldChange(transaction.Code, "Module", original.Module, transaction.Module);
            LogFieldChange(transaction.Code, "Description", original.Description, transaction.Description);
            LogFieldChange(transaction.Code, "HandlerKey", original.HandlerKey, transaction.HandlerKey);
            LogFieldChange(transaction.Code, "RequiresArticleNumber", original.RequiresArticleNumber.ToString(), transaction.RequiresArticleNumber.ToString());
            LogFieldChange(transaction.Code, "IsActive", original.IsActive.ToString(), transaction.IsActive.ToString());
            LogFieldChange(transaction.Code, "Roles", original.RolesText, transaction.RolesText);
        }
    }

    private void LogFieldChange(
        string transactionCode,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "SYS11",
            "Transaction",
            transactionCode,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private void CommitGridEdits()
    {
        GridTransactions.CommitEdit(DataGridEditingUnit.Cell, true);
        GridTransactions.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private string LocalizeTransactionName(TransactionEditorItem transaction)
    {
        return TranslateWithFallback($"Transaction.{transaction.Code}.Name", transaction.Name);
    }

    private string LocalizeTransactionDescription(TransactionEditorItem transaction)
    {
        return TranslateWithFallback($"Transaction.{transaction.Code}.Description", transaction.Description);
    }

    private string LocalizeModuleName(string? moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return string.Empty;
        }

        return TranslateWithFallback($"Module.{moduleName}", moduleName);
    }

    private string TranslateWithFallback(string key, string fallback)
    {
        var translated = _translate?.Invoke(key);

        return IsMissingTranslation(translated, key)
            ? fallback
            : translated!;
    }

    private string T(string key, string fallback)
    {
        var translated = _translate?.Invoke(key);

        return IsMissingTranslation(translated, key)
            ? fallback
            : translated!;
    }

    private string T(string key, string fallback, params object[] args)
    {
        var translated = _translateFormat?.Invoke(key, args);

        if (!IsMissingTranslation(translated, key))
        {
            return translated!;
        }

        return string.Format(CultureInfo.CurrentCulture, fallback, args);
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class Sys11ModuleSelectionItem
{
    public string RawName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
