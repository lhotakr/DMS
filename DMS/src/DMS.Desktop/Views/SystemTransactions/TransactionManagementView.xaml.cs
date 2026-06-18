using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.Configuration.Transactions;
using DMS.Desktop.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemTransactions;

public partial class TransactionManagementView : UserControl, IUnsavedChangesGuard
{
    private readonly string _rolesPath;
    private readonly string _modulesPath;
    private readonly ObservableCollection<string> _availableModuleNames = new();
    private readonly TransactionManagementService _service;
    private readonly Action? _afterSave;
    private readonly ObservableCollection<TransactionEditorItem> _transactions = new();
    private ICollectionView? _view;

    public bool HasUnsavedChanges => _transactions.Any(x => x.State != "Unchanged");

    public TransactionManagementView(
        string transactionsPath,
        string rolesPath,
        string modulesPath,
        Action? afterSave = null)
    {
        InitializeComponent();

        _rolesPath = rolesPath;
        _modulesPath = modulesPath;
        _service = new TransactionManagementService(transactionsPath);
        _afterSave = afterSave;

        GridTransactions.ItemsSource = _transactions;

        LoadModules();
        LoadTransactions();
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            "SYS11 - Neuložené změny",
            "Ve správě transakcí jsou neuložené změny.\n\nChceš opravdu pokračovat bez uložení?");
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

        TxtStatus.Text = $"Načteno transakcí: {_transactions.Count}";
    }

    private void LoadModules()
    {
        _availableModuleNames.Clear();

        var moduleService = new DmsModuleManagementService(_modulesPath);

        var modules = moduleService.LoadAll()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();

        foreach (var module in modules)
        {
            _availableModuleNames.Add(module.Name);
        }

        CmbModuleColumn.ItemsSource = _availableModuleNames;
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
               || Contains(transaction.Module, filter)
               || Contains(transaction.Description, filter)
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
        if (HasUnsavedChanges)
        {
            var confirmUnsaved = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "SYS11 - Znovu načíst",
                "V tabulce jsou neuložené změny.\n\nChceš je zahodit a znovu načíst transactions.json?");

            if (!confirmUnsaved)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "SYS11 - Znovu načíst",
                "Chceš znovu načíst transactions.json?");

            if (!confirm)
            {
                return;
            }
        }

        LoadModules();
        LoadTransactions();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateTransactions();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS11 - Kontrola transakcí",
                validationMessage);

            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            "SYS11 - Uložit",
            "Chceš uložit změny do transactions.json?\n\nPo uložení se obnoví seznam transakcí a levé menu.");

        if (!confirm)
        {
            return;
        }

        try
        {
            _service.SaveAll(_transactions.Where(x => x.State != "Deleted"));

            foreach (var transaction in _transactions)
            {
                transaction.MarkUnchanged();
            }

            _afterSave?.Invoke();

            TxtStatus.Text = $"Uloženo transakcí: {_transactions.Count(x => x.State != "Deleted")} | {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS11",
                "Transakce byly uloženy.");

            LoadModules();
            LoadTransactions();
        }
        catch (Exception ex)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS11 - Chyba",
                $"Uložení transakcí selhalo:\n\n{ex.Message}");
        }
    }

    private void BtnEditRoles_Click(object sender, RoutedEventArgs e)
    {
        if (GridTransactions.SelectedItem is not TransactionEditorItem transaction)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS11 - Role transakce",
                "Vyber transakci, u které chceš upravit role.");

            return;
        }

        if (transaction.State == "Deleted")
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS11 - Role transakce",
                "Role nelze upravovat u transakce označené ke smazání.");

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
                "SYS11 - Výběr rolí",
                "Není dostupná žádná aktivní role. Zkontroluj Config\\dms-roles.json nebo správu rolí SYS12.");

            return;
        }

        var dialog = new RoleSelectionWindow(
            roles,
            transaction.Roles)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            transaction.Roles = dialog.SelectedRoleCodes;
            transaction.MarkModified();

            CollectionViewSource.GetDefaultView(GridTransactions.ItemsSource)?.Refresh();
            GridTransactions.Items.Refresh();

            TxtStatus.Text = $"Role upraveny pro transakci {transaction.Code}. Nezapomeň uložit změny.";
        }
    }

    private string? ValidateTransactions()
    {
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in _transactions.Where(x => x.State != "Deleted"))
        {
            if (string.IsNullOrWhiteSpace(transaction.Code))
            {
                return "Transakce nesmí mít prázdný kód.";
            }

            if (!usedCodes.Add(transaction.Code.Trim()))
            {
                return $"Duplicitní kód transakce: {transaction.Code}";
            }

            if (string.IsNullOrWhiteSpace(transaction.Name))
            {
                return $"Transakce {transaction.Code} musí mít vyplněný název.";
            }

            if (string.IsNullOrWhiteSpace(transaction.Module))
            {
                return $"Transakce {transaction.Code} musí mít vyplněný modul.";
            }

            if (string.IsNullOrWhiteSpace(transaction.HandlerKey))
            {
                return $"Transakce {transaction.Code} musí mít vyplněný HandlerKey.";
            }

            if (transaction.Roles.Any(string.IsNullOrWhiteSpace))
            {
                return $"Transakce {transaction.Code} obsahuje prázdnou roli.";
            }
        }

        return null;
    }
}