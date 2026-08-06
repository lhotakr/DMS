using System;
using System.Collections.Generic;
using System.Linq;
using DMS.Core.Security;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Organization;
using DMS.Desktop.Services.MasterData;
using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.Admin;

public sealed class Usr01PersonChoice
{
    public Guid? PersonId { get; init; }

    public string DisplayText { get; init; } = string.Empty;

    public override string ToString()
    {
        return DisplayText;
    }
}

public partial class UserManagementView : UserControl, IUnsavedChangesGuard
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _usersFilePath;
    private readonly string _rolesFilePath;
    private readonly DmsUserContext _currentUser;
    private readonly DmsRoleManagementService _roleService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName = "UNKNOWN";
    private readonly Action? _afterSave;
    private readonly Func<string, string> _translate;
    private readonly Func<string, object[], string> _translateFormat;
    private readonly DmsMasterDataService _masterDataService;

    private List<DmsRoleDefinition> _availableRoles = new();
    private List<DmsUser> _users = new();
    private List<DmsPerson> _people = new();
    private List<DmsOrganizationUnit> _organizationUnits = new();

    private bool _isLoadingUser;
    private bool _suppressSelectionChanged;
    private bool _hasUnsavedChanges;
    private string? _selectedWindowsLogin;
    private bool _isViewReady;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public UserManagementView(
        string usersFilePath,
        DmsUserContext currentUser)
        : this(
            usersFilePath,
            Path.Combine(AppContext.BaseDirectory, "Config", "dms-roles.json"),
            currentUser,
            null,
            null,
            null,
            null,
            null)
    {
    }

    public UserManagementView(
        string usersFilePath,
        string rolesFilePath,
        DmsUserContext currentUser,
        DmsLogger? logger = null,
        Action? afterSave = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null,
        string? masterDataRootPath = null)
    {
        InitializeComponent();

        _usersFilePath = usersFilePath;
        _rolesFilePath = rolesFilePath;
        _currentUser = currentUser;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUser.DisplayName)
            ? "UNKNOWN"
            : currentUser.DisplayName;
        _afterSave = afterSave;
        _translate = translate ?? (key => key);
        _translateFormat = translateFormat ?? ((key, args) => FormatFallback(_translate(key), args));

        _roleService = new DmsRoleManagementService(_rolesFilePath);
        var resolvedMasterDataRoot = string.IsNullOrWhiteSpace(masterDataRootPath)
            ? Path.Combine(AppContext.BaseDirectory, "Data", "MasterData")
            : masterDataRootPath;
        _masterDataService = new DmsMasterDataService(resolvedMasterDataRoot);

        ApplyLocalization();
        LoadRoles();
        LoadPeople();
        LoadUsers();
        ClearEditor();

        _isViewReady = true;
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("USR01.Dialog.Unsaved.Title"),
            T("USR01.Dialog.Unsaved.NavigationAway"));
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("USR01.Title");
        TxtUsersTitle.Text = T("USR01.UsersTitle");
        TxtUserFilterLabel.Text = T("USR01.Filter");
        BtnNew.Content = T("USR01.New");
        BtnReload.Content = T("USR01.Reload");
        TxtDetailTitle.Text = T("USR01.DetailTitle");
        TxtWindowsLoginLabel.Text = T("USR01.WindowsLogin");
        TxtWindowsLogin.ToolTip = T("USR01.WindowsLogin.ToolTip");
        TxtDisplayNameLabel.Text = T("USR01.DisplayName");
        TxtEmailLabel.Text = T("USR01.Email");
        TxtLinkedPersonLabel.Text = T("USR01.LinkedPerson");
        ChkIsActive.Content = T("USR01.ActiveUser");
        TxtRolesTitle.Text = T("USR01.RolesTitle");
        TxtRolesNote.Text = T("USR01.RolesNote");
        BtnDelete.Content = T("USR01.Delete");
        BtnSave.Content = T("USR01.Save");
    }

    private void LoadRoles()
    {
        _availableRoles = _roleService.LoadAll()
            .Where(role => role.IsActive)
            .OrderBy(role => role.Code)
            .ToList();

        BuildRoleCheckboxes();

        SetStatus("USR01.Status.RolesLoaded", _availableRoles.Count);
    }

    private void BuildRoleCheckboxes()
    {
        RolePanel.Children.Clear();

        foreach (var role in _availableRoles)
        {
            var checkBox = new CheckBox
            {
                Tag = role.Code,
                Margin = new Thickness(0, 4, 0, 4)
            };

            var contentPanel = new StackPanel();

            contentPanel.Children.Add(new TextBlock
            {
                Text = role.Code,
                FontWeight = FontWeights.Bold,
                Foreground = TryFindResource("DmsForegroundBrush") as Brush
            });

            var localizedName = RoleName(role);
            if (!string.IsNullOrWhiteSpace(localizedName))
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = localizedName,
                    Foreground = TryFindResource("DmsMutedForegroundBrush") as Brush
                });
            }

            var localizedDescription = RoleDescription(role);
            if (!string.IsNullOrWhiteSpace(localizedDescription))
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = localizedDescription,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TryFindResource("DmsMutedForegroundBrush") as Brush
                });
            }

            checkBox.Content = contentPanel;
            checkBox.Checked += EditorChanged;
            checkBox.Unchecked += EditorChanged;

            RolePanel.Children.Add(checkBox);
        }
    }

    private void LoadPeople()
    {
        try
        {
            _people = _masterDataService.LoadPeople();
            _organizationUnits = _masterDataService.LoadOrganizationUnits();

            var choices = new List<Usr01PersonChoice>
            {
                new()
                {
                    PersonId = null,
                    DisplayText = T("USR01.LinkedPerson.None")
                }
            };

            choices.AddRange(_people
                .Where(person => person.IsActive)
                .OrderBy(person => person.LastName)
                .ThenBy(person => person.FirstName)
                .Select(person =>
                {
                    var unit = _organizationUnits.FirstOrDefault(item =>
                        item.OrganizationUnitId == person.OrganizationUnitId);

                    var unitName = unit?.Name ?? T("USR01.LinkedPerson.UnknownUnit");

                    return new Usr01PersonChoice
                    {
                        PersonId = person.PersonId,
                        DisplayText = $"{person.PersonnelNumber} — {person.DisplayName} — {unitName}"
                    };
                }));

            CmbLinkedPerson.ItemsSource = choices;
            CmbLinkedPerson.SelectedIndex = 0;

            _logger?.AdminAction(
                "USR01",
                "PeopleLoaded",
                _currentUserName,
                $"Count={_people.Count}; Path={_masterDataService.PeoplePath}");
        }
        catch (Exception ex)
        {
            _people = new List<DmsPerson>();
            _organizationUnits = new List<DmsOrganizationUnit>();
            CmbLinkedPerson.ItemsSource = new[]
            {
                new Usr01PersonChoice
                {
                    PersonId = null,
                    DisplayText = T("USR01.LinkedPerson.None")
                }
            };
            CmbLinkedPerson.SelectedIndex = 0;

            _logger?.Error($"USR01 failed to load people from {_masterDataService.PeoplePath}", ex);
        }
    }

    private void LoadUsers()
    {
        _logger?.AdminAction(
            "USR01",
            "LoadUsers",
            _currentUserName,
            $"Path={_usersFilePath}");

        if (!File.Exists(_usersFilePath))
        {
            _users = new List<DmsUser>();
            RefreshUserList();

            SetStatus("USR01.Status.UsersFileMissing", _usersFilePath);

            _logger?.Warning($"USR01 users file not found: {_usersFilePath}");

            return;
        }

        try
        {
            var json = File.ReadAllText(_usersFilePath, Encoding.UTF8);

            _users = JsonSerializer.Deserialize<List<DmsUser>>(
                json,
                JsonOptions) ?? new List<DmsUser>();

            RefreshUserList();

            SetStatus("USR01.Status.UsersLoaded", _users.Count, _usersFilePath);

            _logger?.AdminAction(
                "USR01",
                "UsersLoaded",
                _currentUserName,
                $"Count={_users.Count}; Path={_usersFilePath}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"USR01 failed to load users from {_usersFilePath}", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("USR01.Dialog.LoadError.Title"),
                Tf("USR01.Dialog.LoadError.Message", ex.Message, _usersFilePath));

            _users = new List<DmsUser>();
            RefreshUserList();
        }
    }

    private void RefreshUserList()
    {
        var previouslySelectedLogin = _selectedWindowsLogin;
        var filter = TxtUserFilter?.Text?.Trim();

        IEnumerable<DmsUser> users = _users;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            users = users.Where(user =>
                Contains(user.WindowsLogin, filter) ||
                Contains(user.DisplayName, filter) ||
                Contains(user.Email, filter) ||
                user.Roles.Any(role => Contains(role, filter)) ||
                PersonMatchesFilter(user.PersonId, filter));
        }

        LstUsers.ItemsSource = null;
        LstUsers.ItemsSource = users
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.DisplayName)
            .ThenBy(user => user.WindowsLogin)
            .ToList();

        if (!string.IsNullOrWhiteSpace(previouslySelectedLogin))
        {
            var selectedUser = _users.FirstOrDefault(user =>
                string.Equals(user.WindowsLogin, previouslySelectedLogin, StringComparison.OrdinalIgnoreCase));

            if (selectedUser is not null)
            {
                _suppressSelectionChanged = true;
                LstUsers.SelectedItem = selectedUser;
                _suppressSelectionChanged = false;
            }
        }
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool PersonMatchesFilter(Guid? personId, string filter)
    {
        if (!personId.HasValue)
        {
            return false;
        }

        var person = _people.FirstOrDefault(item => item.PersonId == personId.Value);
        if (person is null)
        {
            return false;
        }

        return Contains(person.PersonnelNumber, filter)
               || Contains(person.FirstName, filter)
               || Contains(person.LastName, filter)
               || Contains(person.DisplayName, filter);
    }

    private void TxtUserFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshUserList();
    }

    private void LstUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        if (LstUsers.SelectedItem is not DmsUser user)
        {
            return;
        }

        if (HasUnsavedChanges)
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("USR01.Dialog.Unsaved.Title"),
                T("USR01.Dialog.Unsaved.SwitchUser"));

            if (!confirm)
            {
                RestorePreviousSelection();
                return;
            }
        }

        ShowUser(user);
    }

    private void RestorePreviousSelection()
    {
        if (string.IsNullOrWhiteSpace(_selectedWindowsLogin))
        {
            return;
        }

        var previousUser = _users.FirstOrDefault(user =>
            string.Equals(user.WindowsLogin, _selectedWindowsLogin, StringComparison.OrdinalIgnoreCase));

        if (previousUser is null)
        {
            return;
        }

        _suppressSelectionChanged = true;
        LstUsers.SelectedItem = previousUser;
        _suppressSelectionChanged = false;
    }

    private void ShowUser(DmsUser user)
    {
        _isLoadingUser = true;

        try
        {
            _selectedWindowsLogin = user.WindowsLogin;

            TxtWindowsLogin.Text = user.WindowsLogin;
            TxtDisplayName.Text = user.DisplayName;
            TxtEmail.Text = user.Email;
            SelectLinkedPerson(user.PersonId);
            ChkIsActive.IsChecked = user.IsActive;

            foreach (var child in RolePanel.Children)
            {
                if (child is not CheckBox checkBox)
                {
                    continue;
                }

                var roleCode = checkBox.Tag?.ToString() ?? string.Empty;

                checkBox.IsChecked = user.Roles.Any(userRole =>
                    string.Equals(userRole, roleCode, StringComparison.OrdinalIgnoreCase));
            }

            _hasUnsavedChanges = false;
            SetStatus("USR01.Status.UserSelected", user.DisplayName);
        }
        finally
        {
            _isLoadingUser = false;
        }
    }

    private void ClearEditor()
    {
        _isLoadingUser = true;

        try
        {
            _selectedWindowsLogin = null;

            TxtWindowsLogin.Text = string.Empty;
            TxtDisplayName.Text = string.Empty;
            TxtEmail.Text = string.Empty;
            CmbLinkedPerson.SelectedIndex = 0;
            ChkIsActive.IsChecked = true;

            foreach (var child in RolePanel.Children)
            {
                if (child is CheckBox checkBox)
                {
                    checkBox.IsChecked = false;
                }
            }

            _hasUnsavedChanges = false;
        }
        finally
        {
            _isLoadingUser = false;
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges)
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("USR01.Dialog.NewUser.Title"),
                T("USR01.Dialog.NewUser.DiscardCurrent"));

            if (!confirm)
            {
                return;
            }
        }

        _suppressSelectionChanged = true;
        LstUsers.SelectedItem = null;
        _suppressSelectionChanged = false;

        ClearEditor();

        SetStatus("USR01.Status.NewUser");
        TxtWindowsLogin.Focus();

        _logger?.AdminAction(
            "USR01",
            "NewUserEditor",
            _currentUserName,
            "OpenedEmptyEditor=True");
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges)
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("USR01.Dialog.Reload.Title"),
                T("USR01.Dialog.Reload.DiscardChanges"));

            if (!confirm)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("USR01.Dialog.Reload.Title"),
                T("USR01.Dialog.Reload.Confirm"));

            if (!confirm)
            {
                return;
            }
        }

        _logger?.AdminAction(
            "USR01",
            "ReloadUsers",
            _currentUserName,
            $"HadUnsavedChanges={HasUnsavedChanges}");

        LoadRoles();
        LoadPeople();
        LoadUsers();
        ClearEditor();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var windowsLogin = TxtWindowsLogin.Text.Trim();
        var displayName = TxtDisplayName.Text.Trim();
        var email = TxtEmail.Text.Trim();

        if (string.IsNullOrWhiteSpace(windowsLogin))
        {
            ValidationInfo("USR01.Validation.MissingWindowsLogin", "Reason=MissingWindowsLogin");
            TxtWindowsLogin.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = windowsLogin;
        }

        var selectedRoles = GetSelectedRoles();
        var selectedPersonId = (CmbLinkedPerson.SelectedItem as Usr01PersonChoice)?.PersonId;

        if (selectedRoles.Count == 0)
        {
            ValidationInfo("USR01.Validation.MissingRole", "Reason=MissingRole");
            return;
        }

        var duplicateLogin = _users.Any(user =>
            !string.Equals(user.WindowsLogin, _selectedWindowsLogin, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        if (duplicateLogin)
        {
            ValidationInfo(
                "USR01.Validation.DuplicateWindowsLogin",
                $"Reason=DuplicateWindowsLogin; WindowsLogin={windowsLogin}",
                windowsLogin);

            return;
        }

        if (selectedPersonId.HasValue)
        {
            var duplicatePerson = _users.Any(user =>
                user.IsActive
                && user.PersonId == selectedPersonId
                && !string.Equals(user.WindowsLogin, _selectedWindowsLogin, StringComparison.OrdinalIgnoreCase));

            if (duplicatePerson)
            {
                ValidationInfo(
                    "USR01.Validation.DuplicateLinkedPerson",
                    $"Reason=DuplicateLinkedPerson; PersonId={selectedPersonId}",
                    GetPersonDisplayText(selectedPersonId));
                return;
            }
        }

        var existingIndex = _users.FindIndex(user =>
            string.Equals(user.WindowsLogin, _selectedWindowsLogin ?? windowsLogin, StringComparison.OrdinalIgnoreCase));

        var savedUser = new DmsUser
        {
            WindowsLogin = windowsLogin,
            DisplayName = displayName,
            Email = email,
            PersonId = selectedPersonId,
            IsActive = ChkIsActive.IsChecked == true,
            Roles = selectedRoles
        };

        var originalUser = existingIndex >= 0
            ? _users[existingIndex]
            : null;

        if (existingIndex >= 0)
        {
            _users[existingIndex] = savedUser;
        }
        else
        {
            _users.Add(savedUser);
        }

        try
        {
            SaveUsers();
        }
        catch (Exception ex)
        {
            _logger?.Error($"USR01 failed to save users to {_usersFilePath}", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("USR01.Dialog.SaveError.Title"),
                Tf("USR01.Dialog.SaveError.Message", ex.Message, _usersFilePath));

            return;
        }

        LogUserChanges(originalUser, savedUser);

        _logger?.AdminAction(
            "USR01",
            "SaveUser",
            _currentUserName,
            $"WindowsLogin={windowsLogin}; Roles={string.Join(",", selectedRoles)}; IsActive={savedUser.IsActive}");

        _selectedWindowsLogin = savedUser.WindowsLogin;
        _hasUnsavedChanges = false;

        RefreshUserList();
        ShowUser(savedUser);
        _afterSave?.Invoke();

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("USR01.Dialog.Saved.Title"),
            Tf("USR01.Dialog.Saved.Message", displayName));
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var windowsLogin = TxtWindowsLogin.Text.Trim();

        if (string.IsNullOrWhiteSpace(windowsLogin))
        {
            return;
        }

        if (string.Equals(windowsLogin, _currentUser.WindowsLogin, StringComparison.OrdinalIgnoreCase))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("USR01.Dialog.DeleteCurrentUser.Title"),
                T("USR01.Dialog.DeleteCurrentUser.Message"));

            _logger?.AdminAction(
                "USR01",
                "DeleteCurrentUserDenied",
                _currentUserName,
                $"WindowsLogin={windowsLogin}");

            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("USR01.Dialog.Delete.Title"),
            Tf("USR01.Dialog.Delete.Confirm", windowsLogin));

        if (!confirm)
        {
            return;
        }

        var deletedUser = _users.FirstOrDefault(user =>
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        if (deletedUser is not null)
        {
            _logger?.AuditDeleted(
                "USR01",
                "User",
                deletedUser.WindowsLogin,
                _currentUserName,
                $"DisplayName={deletedUser.DisplayName}; Email={deletedUser.Email}; Roles={string.Join(",", deletedUser.Roles)}");
        }

        _users.RemoveAll(user =>
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        try
        {
            SaveUsers();
        }
        catch (Exception ex)
        {
            _logger?.Error($"USR01 failed to save users after delete to {_usersFilePath}", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("USR01.Dialog.SaveError.Title"),
                Tf("USR01.Dialog.SaveError.Message", ex.Message, _usersFilePath));

            return;
        }

        RefreshUserList();
        ClearEditor();
        _afterSave?.Invoke();

        SetStatus("USR01.Status.UserDeleted", windowsLogin);
    }

    private void SelectLinkedPerson(Guid? personId)
    {
        var choice = CmbLinkedPerson.Items
            .OfType<Usr01PersonChoice>()
            .FirstOrDefault(item => item.PersonId == personId);

        CmbLinkedPerson.SelectedItem = choice ?? CmbLinkedPerson.Items
            .OfType<Usr01PersonChoice>()
            .FirstOrDefault(item => item.PersonId is null);
    }

    private string GetPersonDisplayText(Guid? personId)
    {
        if (!personId.HasValue)
        {
            return T("USR01.LinkedPerson.None");
        }

        return CmbLinkedPerson.Items
                   .OfType<Usr01PersonChoice>()
                   .FirstOrDefault(item => item.PersonId == personId)?.DisplayText
               ?? personId.Value.ToString();
    }

    private List<string> GetSelectedRoles()
    {
        var roles = new List<string>();

        foreach (var child in RolePanel.Children)
        {
            if (child is not CheckBox checkBox)
            {
                continue;
            }

            if (checkBox.IsChecked != true)
            {
                continue;
            }

            var role = checkBox.Tag?.ToString();

            if (!string.IsNullOrWhiteSpace(role))
            {
                roles.Add(role.Trim().ToUpperInvariant());
            }
        }

        return roles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToList();
    }

    private void SaveUsers()
    {
        var directory = Path.GetDirectoryName(_usersFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            _users
                .OrderBy(user => user.DisplayName)
                .ThenBy(user => user.WindowsLogin)
                .ToList(),
            JsonOptions);

        File.WriteAllText(_usersFilePath, json, Encoding.UTF8);
    }

    private void EditorChanged(object sender, RoutedEventArgs e)
    {
        if (!_isViewReady || _isLoadingUser)
        {
            return;
        }

        _hasUnsavedChanges = true;

        if (TxtStatus is not null)
        {
            SetStatus("USR01.Status.UnsavedDetail");
        }
    }

    private void LogUserChanges(DmsUser? originalUser, DmsUser savedUser)
    {
        if (originalUser is null)
        {
            _logger?.AuditCreated(
                "USR01",
                "User",
                savedUser.WindowsLogin,
                _currentUserName,
                $"DisplayName={savedUser.DisplayName}; Email={savedUser.Email}; PersonId={savedUser.PersonId}; IsActive={savedUser.IsActive}; Roles={string.Join(",", savedUser.Roles)}");

            return;
        }

        LogUserFieldChange(savedUser.WindowsLogin, "WindowsLogin", originalUser.WindowsLogin, savedUser.WindowsLogin);
        LogUserFieldChange(savedUser.WindowsLogin, "DisplayName", originalUser.DisplayName, savedUser.DisplayName);
        LogUserFieldChange(savedUser.WindowsLogin, "Email", originalUser.Email, savedUser.Email);
        LogUserFieldChange(savedUser.WindowsLogin, "PersonId", originalUser.PersonId?.ToString(), savedUser.PersonId?.ToString());
        LogUserFieldChange(savedUser.WindowsLogin, "IsActive", originalUser.IsActive.ToString(), savedUser.IsActive.ToString());

        var oldRoles = string.Join(",", originalUser.Roles.OrderBy(x => x));
        var newRoles = string.Join(",", savedUser.Roles.OrderBy(x => x));

        LogUserFieldChange(savedUser.WindowsLogin, "Roles", oldRoles, newRoles);
    }

    private void LogUserFieldChange(
        string windowsLogin,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "USR01",
            "User",
            windowsLogin,
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private string RoleName(DmsRoleDefinition role)
    {
        var key = $"Role.{role.Code}.Name";
        var translated = T(key);

        return IsMissingTranslation(translated, key)
            ? role.Name
            : translated;
    }

    private string RoleDescription(DmsRoleDefinition role)
    {
        var key = $"Role.{role.Code}.Description";
        var translated = T(key);

        return IsMissingTranslation(translated, key)
            ? role.Description
            : translated;
    }

    private void ValidationInfo(string messageKey, string logDetail, params object[] args)
    {
        _logger?.AdminAction(
            "USR01",
            "ValidationFailed",
            _currentUserName,
            logDetail);

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("USR01.Dialog.Validation.Title"),
            Tf(messageKey, args));
    }

    private void SetStatus(string key, params object[] args)
    {
        TxtStatus.Text = Tf(key, args);
    }

    private string T(string key)
    {
        var value = _translate(key);
        return IsMissingTranslation(value, key) ? key : value;
    }

    private string Tf(string key, params object[] args)
    {
        var value = _translateFormat(key, args);

        if (IsMissingTranslation(value, key))
        {
            value = _translate(key);
        }

        if (IsMissingTranslation(value, key))
        {
            return key;
        }

        try
        {
            return args.Length == 0
                ? value
                : string.Format(value, args);
        }
        catch
        {
            return value;
        }
    }

    private static string FormatFallback(string format, object[] args)
    {
        try
        {
            return args.Length == 0
                ? format
                : string.Format(format, args);
        }
        catch
        {
            return format;
        }
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
