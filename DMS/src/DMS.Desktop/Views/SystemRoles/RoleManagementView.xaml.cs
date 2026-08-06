using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.Localization;
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

namespace DMS.Desktop.Views.SystemRoles;

public partial class RoleManagementView : UserControl, IUnsavedChangesGuard
{
    private const string Area = "SYS12";

    private readonly DmsRoleManagementService _service;
    private readonly ObservableCollection<DmsRoleDefinition> _roles = new();
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string> _translate;
    private readonly Func<string, object[], string> _translateFormat;

    private ICollectionView? _view;
    private bool _isLoading;
    private List<DmsRoleDefinition> _originalRoles = new();

    public bool HasUnsavedChanges => _roles.Any(x => x.State != "Unchanged");

    public RoleManagementView(string rolesPath)
        : this(
            rolesPath,
            null,
            null,
            null,
            null)
    {
    }

    public RoleManagementView(
        string rolesPath,
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

        _translate = translate ?? DefaultTranslate;
        _translateFormat = translateFormat ?? DefaultTranslateFormat;

        Sys12RoleTextConverter.Translate = T;

        _service = new DmsRoleManagementService(rolesPath);

        _roles.CollectionChanged += Roles_CollectionChanged;
        GridRoles.ItemsSource = _roles;

        ApplyLocalization();
        LoadRoles();

        _logger?.AdminAction(
            Area,
            "OpenRoleManagement",
            _currentUserName,
            $"RolesPath={rolesPath}");
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("SYS12.Dialog.UnsavedTitle"),
            T("SYS12.Dialog.UnsavedMessage"));
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SYS12.Title");
        TxtCardTitle.Text = T("SYS12.CardTitle");
        TxtTechnicalNote.Text = T("SYS12.TechnicalNote");
        TxtFilterLabel.Text = T("SYS12.FilterLabel");
        ChkShowInactive.Content = T("SYS12.ShowInactive");

        ColCode.Header = T("SYS12.Column.Code");
        ColName.Header = T("SYS12.Column.Name");
        ColDescription.Header = T("SYS12.Column.Description");
        ColIsActive.Header = T("SYS12.Column.IsActive");

        BtnReload.Content = T("SYS12.Button.Reload");
        BtnSave.Content = T("SYS12.Button.Save");
        BtnMarkDeleted.Content = T("SYS12.Button.MarkDeleted");
        BtnRestoreRow.Content = T("SYS12.Button.RestoreRow");
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

            _originalRoles = _roles
                .Select(CloneRole)
                .ToList();

            _view = CollectionViewSource.GetDefaultView(_roles);
            _view.Filter = FilterRole;

            TxtStatus.Text = T("SYS12.Status.Loaded", _roles.Count);

            _logger?.AdminAction(
                Area,
                "LoadRoles",
                _currentUserName,
                $"Count={_roles.Count}");
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
               || Contains(role.State, filter)
               || Contains(DmsRoleText.Name(role, T), filter)
               || Contains(DmsRoleText.Description(role, T), filter);
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
                T("SYS12.Dialog.ReloadTitle"),
                T("SYS12.Dialog.ReloadUnsavedMessage"));

            if (!confirmUnsaved)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("SYS12.Dialog.ReloadTitle"),
                T("SYS12.Dialog.ReloadMessage"));

            if (!confirm)
            {
                return;
            }
        }

        _logger?.AdminAction(
            Area,
            "ReloadRoles",
            _currentUserName,
            $"HadUnsavedChanges={HasUnsavedChanges}");

        LoadRoles();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        var validationMessage = ValidateRoles();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            _logger?.AdminAction(
                Area,
                "ValidationFailed",
                _currentUserName,
                "Role validation failed.");

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS12.Dialog.ValidationTitle"),
                validationMessage);

            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("SYS12.Dialog.SaveTitle"),
            T("SYS12.Dialog.SaveMessage"));

        if (!confirm)
        {
            return;
        }

        try
        {
            LogRoleChanges();

            var savedCount = _roles.Count(x => x.State != "Deleted");
            var deletedCount = _roles.Count(x => x.State == "Deleted");
            var addedCount = _roles.Count(x => x.State == "Added");
            var modifiedCount = _roles.Count(x => x.State == "Modified");

            _service.SaveAll(_roles.Where(x => x.State != "Deleted"));

            _logger?.AdminAction(
                Area,
                "SaveRoles",
                _currentUserName,
                $"Saved={savedCount}; Added={addedCount}; Modified={modifiedCount}; Deleted={deletedCount}");

            TxtStatus.Text = T(
                "SYS12.Status.Saved",
                savedCount,
                DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS12.Dialog.SaveSuccessTitle"),
                T("SYS12.Dialog.SaveSuccessMessage"));

            LoadRoles();
        }
        catch (Exception ex)
        {
            _logger?.Error("SYS12: Save roles failed.", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS12.Dialog.ErrorTitle"),
                T("SYS12.Dialog.SaveFailedMessage", ex.Message));
        }
    }

    private void BtnMarkDeleted_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        if (GridRoles.SelectedItem is not DmsRoleDefinition role)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS12.Dialog.DeleteTitle"),
                T("SYS12.Dialog.DeleteSelectRoleMessage"));

            return;
        }

        if (role.State == "Deleted")
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("SYS12.Dialog.DeleteTitle"),
                T("SYS12.Dialog.DeleteAlreadyMarkedMessage", role.Code));

            return;
        }

        role.MarkDeleted();
        RefreshGridView();

        _logger?.AdminAction(
            Area,
            "MarkRoleDeleted",
            _currentUserName,
            $"Role={role.Code}");

        TxtStatus.Text = T("SYS12.Status.MarkedDeleted", role.Code);
    }

    private void BtnRestoreRow_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdit();

        if (GridRoles.SelectedItem is not DmsRoleDefinition role)
        {
            return;
        }

        if (role.State == "Added")
        {
            _roles.Remove(role);
            TxtStatus.Text = T("SYS12.Status.AddedRemoved");
            return;
        }

        RestoreRoleState(role);

        RefreshGridView();
        TxtStatus.Text = T("SYS12.Status.Restored", role.Code);
    }

    private void RestoreRoleState(DmsRoleDefinition role)
    {
        var original = _originalRoles.FirstOrDefault(x =>
            string.Equals(x.Code, role.Code, StringComparison.OrdinalIgnoreCase));

        if (original is null)
        {
            role.MarkModified();
            return;
        }

        if (RoleEquals(original, role))
        {
            role.MarkUnchanged();
        }
        else
        {
            role.MarkModified();
        }
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
                return T("SYS12.Validation.EmptyCode");
            }

            if (role.Code.Any(char.IsWhiteSpace))
            {
                return T("SYS12.Validation.CodeContainsWhitespace", role.Code);
            }

            if (!usedCodes.Add(role.Code.Trim()))
            {
                return T("SYS12.Validation.DuplicateCode", role.Code);
            }

            if (string.IsNullOrWhiteSpace(role.Name))
            {
                return T("SYS12.Validation.EmptyName", role.Code);
            }
        }

        return null;
    }

    private void LogRoleChanges()
    {
        foreach (var role in _roles)
        {
            var original = _originalRoles.FirstOrDefault(x =>
                string.Equals(x.Code, role.Code, StringComparison.OrdinalIgnoreCase));

            if (role.State == "Deleted")
            {
                _logger?.AuditDeleted(
                    Area,
                    "Role",
                    role.Code,
                    _currentUserName,
                    $"IsActive={role.IsActive}");

                continue;
            }

            if (original is null)
            {
                _logger?.AuditCreated(
                    Area,
                    "Role",
                    role.Code,
                    _currentUserName,
                    $"IsActive={role.IsActive}");

                continue;
            }

            LogRoleFieldChange(role.Code, "Name", original.Name, role.Name);
            LogRoleFieldChange(role.Code, "Description", original.Description, role.Description);
            LogRoleFieldChange(role.Code, "IsActive", original.IsActive.ToString(), role.IsActive.ToString());
        }
    }

    private void LogRoleFieldChange(
        string roleCode,
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
            "Role",
            roleCode,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private void CommitGridEdit()
    {
        GridRoles.CommitEdit(DataGridEditingUnit.Cell, true);
        GridRoles.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void RefreshGridView()
    {
        CollectionViewSource.GetDefaultView(GridRoles.ItemsSource)?.Refresh();
        _view?.Refresh();
    }

    private static DmsRoleDefinition CloneRole(DmsRoleDefinition source)
    {
        var clone = new DmsRoleDefinition
        {
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            IsActive = source.IsActive
        };

        clone.MarkUnchanged();
        return clone;
    }

    private static bool RoleEquals(
        DmsRoleDefinition a,
        DmsRoleDefinition b)
    {
        return string.Equals(a.Code, b.Code, StringComparison.Ordinal)
               && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
               && string.Equals(a.Description, b.Description, StringComparison.Ordinal)
               && a.IsActive == b.IsActive;
    }

    private string T(string key)
    {
        var value = _translate(key);

        if (!IsMissingTranslation(value, key))
        {
            return value;
        }

        return EnglishFallback.TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    private string T(string key, params object[] args)
    {
        var value = _translateFormat(key, args);

        if (!IsMissingTranslation(value, key))
        {
            return value;
        }

        var format = EnglishFallback.TryGetValue(key, out var fallback)
            ? fallback
            : key;

        return string.Format(format, args);
    }

    private static string DefaultTranslate(string key)
    {
        return key;
    }

    private static string DefaultTranslateFormat(string key, object[] args)
    {
        return key;
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, string> EnglishFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SYS12.Title"] = "SYS12 - Role management",
        ["SYS12.CardTitle"] = "DMS roles",
        ["SYS12.TechnicalNote"] = "Roles are used to control access to transactions. Keep role codes uppercase and without spaces.",
        ["SYS12.FilterLabel"] = "Filter:",
        ["SYS12.ShowInactive"] = "Show inactive",
        ["SYS12.Column.Code"] = "Code",
        ["SYS12.Column.Name"] = "Name",
        ["SYS12.Column.Description"] = "Description",
        ["SYS12.Column.IsActive"] = "Active",
        ["SYS12.Button.Reload"] = "Reload",
        ["SYS12.Button.Save"] = "Save",
        ["SYS12.Button.MarkDeleted"] = "Mark for deletion",
        ["SYS12.Button.RestoreRow"] = "Restore mark",
        ["SYS12.Status.Loaded"] = "Loaded roles: {0}",
        ["SYS12.Status.Saved"] = "Saved roles: {0} | {1}",
        ["SYS12.Status.MarkedDeleted"] = "Role {0} was marked for deletion. Confirm the change with Save.",
        ["SYS12.Status.AddedRemoved"] = "Newly added role was removed from the table.",
        ["SYS12.Status.Restored"] = "Marking for role {0} was restored.",
        ["SYS12.Dialog.UnsavedTitle"] = "SYS12 - Unsaved changes",
        ["SYS12.Dialog.UnsavedMessage"] = "Role management contains unsaved changes.\n\nDo you really want to continue without saving?",
        ["SYS12.Dialog.ReloadTitle"] = "SYS12 - Reload",
        ["SYS12.Dialog.ReloadUnsavedMessage"] = "The table contains unsaved changes.\n\nDiscard them and reload dms-roles.json?",
        ["SYS12.Dialog.ReloadMessage"] = "Reload dms-roles.json?",
        ["SYS12.Dialog.SaveTitle"] = "SYS12 - Save",
        ["SYS12.Dialog.SaveMessage"] = "Save changes to dms-roles.json?",
        ["SYS12.Dialog.ValidationTitle"] = "SYS12 - Role validation",
        ["SYS12.Dialog.SaveSuccessTitle"] = "SYS12",
        ["SYS12.Dialog.SaveSuccessMessage"] = "Roles were saved.",
        ["SYS12.Dialog.ErrorTitle"] = "SYS12 - Error",
        ["SYS12.Dialog.SaveFailedMessage"] = "Saving roles failed:\n\n{0}",
        ["SYS12.Dialog.DeleteTitle"] = "SYS12 - Delete role",
        ["SYS12.Dialog.DeleteSelectRoleMessage"] = "Select the role you want to mark for deletion.",
        ["SYS12.Dialog.DeleteAlreadyMarkedMessage"] = "Role {0} is already marked for deletion.",
        ["SYS12.Validation.EmptyCode"] = "Role code cannot be empty.",
        ["SYS12.Validation.CodeContainsWhitespace"] = "Role code cannot contain spaces: {0}",
        ["SYS12.Validation.DuplicateCode"] = "Duplicate role code: {0}",
        ["SYS12.Validation.EmptyName"] = "Role {0} must have a name."
    };
}
