using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemRoles;

public partial class RoleManagementView : UserControl, IUnsavedChangesGuard
{
    private readonly DmsRoleManagementService _service;
    private readonly ObservableCollection<DmsRoleDefinition> _roles = new();
    private ICollectionView? _view;
    private bool _isLoading;

    public bool HasUnsavedChanges => _roles.Any(x => x.State != "Unchanged");

    public RoleManagementView(string rolesPath)
    {
        InitializeComponent();

        _service = new DmsRoleManagementService(rolesPath);

        _roles.CollectionChanged += Roles_CollectionChanged;
        GridRoles.ItemsSource = _roles;

        LoadRoles();
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            "SYS12 - Neuložené změny",
            "Ve správě rolí jsou neuložené změny.\n\nChceš opravdu pokračovat bez uložení?");
    }

    private void Roles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoading || e.NewItems is null)
        {
            return;
        }

        foreach (var role in e.NewItems.OfType<DmsRoleDefinition>())
        {
            role.MarkAdded();
        }
    }

    private void LoadRoles()
    {
        _isLoading = true;

        try
        {
            _roles.Clear();

            foreach (var role in _service.LoadAll())
            {
                role.MarkUnchanged();
                _roles.Add(role);
            }

            _view = CollectionViewSource.GetDefaultView(_roles);
            _view.Filter = FilterRole;

            TxtStatus.Text = $"Načteno rolí: {_roles.Count}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool FilterRole(object item)
    {
        if (item is not DmsRoleDefinition role)
        {
            return false;
        }

        if (ChkShowInactive.IsChecked != true && !role.IsActive)
        {
            return false;
        }

        var filter = TxtFilter.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(role.Code, filter)
               || Contains(role.Name, filter)
               || Contains(role.Description, filter)
               || Contains(role.State, filter);
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
                "SYS12 - Znovu načíst",
                "V tabulce jsou neuložené změny.\n\nChceš je zahodit a znovu načíst dms-roles.json?");

            if (!confirmUnsaved)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "SYS12 - Znovu načíst",
                "Chceš znovu načíst dms-roles.json?");

            if (!confirm)
            {
                return;
            }
        }

        LoadRoles();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateRoles();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS12 - Kontrola rolí",
                validationMessage);

            return;
        }

        try
        {
            _service.SaveAll(_roles.Where(x => x.State != "Deleted"));

            TxtStatus.Text = $"Uloženo rolí: {_roles.Count(x => x.State != "Deleted")} | {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS12",
                "Role byly uloženy.");

            LoadRoles();
        }
        catch (Exception ex)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS12 - Chyba",
                $"Uložení rolí selhalo:\n\n{ex.Message}");
        }
    }

    private void BtnMarkDeleted_Click(object sender, RoutedEventArgs e)
    {
        if (GridRoles.SelectedItem is not DmsRoleDefinition role)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS12 - Mazání role",
                "Vyber roli, kterou chceš označit ke smazání.");

            return;
        }

        role.MarkDeleted();
        GridRoles.Items.Refresh();

        TxtStatus.Text = $"Role {role.Code} označena ke smazání. Změnu potvrď tlačítkem Uložit.";
    }

    private void BtnRestoreRow_Click(object sender, RoutedEventArgs e)
    {
        if (GridRoles.SelectedItem is not DmsRoleDefinition role)
        {
            return;
        }

        if (role.State == "Added")
        {
            _roles.Remove(role);
        }
        else
        {
            role.MarkModified();
        }

        GridRoles.Items.Refresh();
        TxtStatus.Text = "Označení řádku bylo vráceno.";
    }

    private string? ValidateRoles()
    {
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in _roles.Where(x => x.State != "Deleted"))
        {
            if (string.IsNullOrWhiteSpace(role.Code) &&
                string.IsNullOrWhiteSpace(role.Name) &&
                string.IsNullOrWhiteSpace(role.Description))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(role.Code))
            {
                return "Role nesmí mít prázdný kód.";
            }

            if (role.Code.Any(char.IsWhiteSpace))
            {
                return $"Kód role nesmí obsahovat mezery: {role.Code}";
            }

            if (!usedCodes.Add(role.Code.Trim()))
            {
                return $"Duplicitní kód role: {role.Code}";
            }

            if (string.IsNullOrWhiteSpace(role.Name))
            {
                return $"Role {role.Code} musí mít vyplněný název.";
            }
        }

        return null;
    }
}