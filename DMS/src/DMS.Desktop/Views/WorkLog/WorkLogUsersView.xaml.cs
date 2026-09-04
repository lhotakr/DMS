using DMS.Desktop.Logging;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.WorkLog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.WorkLog;

public partial class WorkLogUsersView : UserControl
{
    private readonly WorkLogSettingsService _settingsService;
    private readonly DmsLogger? _logger;
    private readonly string _windowsLogin;
    private readonly string _currentUserName;
    private readonly bool _isDmsAdmin;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private WorkLogRepository? _repository;
    private WorkLogAccessPolicy? _access;
    private readonly ObservableCollection<WorkLogUser> _users = new();
    private ICollectionView? _usersView;
    private IReadOnlyList<WorkLogUserGroup> _groups =
        Array.Empty<WorkLogUserGroup>();
    private IReadOnlyList<WorkLogUser> _masterUsers =
        Array.Empty<WorkLogUser>();
    private WorkLogUser? _selectedUser;
    private bool _loading;
    private bool _dirty;

    public WorkLogUsersView(
        string configurationRootPath,
        string windowsLogin,
        string currentUserName,
        bool isDmsAdmin,
        DmsLogger? logger = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _settingsService =
            new WorkLogSettingsService(configurationRootPath);
        _windowsLogin = windowsLogin ?? string.Empty;
        _currentUserName =
            string.IsNullOrWhiteSpace(currentUserName)
                ? "UNKNOWN"
                : currentUserName;
        _isDmsAdmin = isDmsAdmin;
        _logger = logger;
        _translate = translate;
        _translateFormat = translateFormat;

        GridUsers.ItemsSource = _users;

        ApplyLocalization();
        BuildAccessOptions();
        BuildUserTypeOptions();
        LoadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("WLUSERS.Title");
        TxtSubtitle.Text = T("WLUSERS.Subtitle");
        LblFilter.Text = T("WLUSERS.Filter");
        ChkShowArchived.Content = T("WLUSERS.ShowArchived");
        BtnReload.Content = T("WLUSERS.Reload");
        TxtEditorTitle.Text = T("WLUSERS.Editor");
        LblFirstName.Text = T("WLUSERS.FirstName");
        LblSurname.Text = T("WLUSERS.Surname");
        LblPersonalNumber.Text = T("WLUSERS.PersonalNumber");
        LblWindowsUsername.Text = T("WLUSERS.WindowsUsername");
        LblEmail.Text = T("WLUSERS.Email");
        LblAccess.Text = T("WLUSERS.Access");
        LblType.Text = T("WLUSERS.Type");
        LblGroup.Text = T("WLUSERS.Group");
        LblMasterUser.Text = T("WLUSERS.MasterUser");
        BtnNew.Content = T("WLUSERS.New");
        BtnSave.Content = T("WLUSERS.Save");
        BtnArchive.Content = T("WLUSERS.Archive");
        BtnRestore.Content = T("WLUSERS.Restore");

        ColSurname.Header = T("WLUSERS.Col.Surname");
        ColFirstName.Header = T("WLUSERS.Col.FirstName");
        ColPersonalNumber.Header = T("WLUSERS.Col.PersonalNumber");
        ColWindows.Header = T("WLUSERS.Col.Windows");
        ColGroup.Header = T("WLUSERS.Col.Group");
        ColAccess.Header = T("WLUSERS.Col.Access");
        ColArchived.Header = T("WLUSERS.Col.Archived");
    }

    private void BuildAccessOptions()
    {
        CmbAccess.ItemsSource = new[]
        {
            new AccessOption(0, T("WLUSERS.Access.0")),
            new AccessOption(1, T("WLUSERS.Access.1")),
            new AccessOption(2, T("WLUSERS.Access.2")),
            new AccessOption(3, T("WLUSERS.Access.3"))
        };
        CmbAccess.DisplayMemberPath = nameof(AccessOption.Text);
        CmbAccess.SelectedValuePath = nameof(AccessOption.Value);
    }

    private void BuildUserTypeOptions()
    {
        CmbUserType.ItemsSource = new[]
        {
            new UserTypeOption(
                false,
                T("WLUSERS.Type.Internal")),
            new UserTypeOption(
                true,
                T("WLUSERS.Type.External"))
        };
    }

    private void LoadData(int? selectUserId = null)
    {
        _loading = true;

        try
        {
            var settings = _settingsService.Load();
            _repository =
                new WorkLogRepository(settings.DatabasePath);
            _repository.TestConnection();

            var current =
                _repository.FindUserByWindowsUsername(
                    _windowsLogin);

            _access =
                new WorkLogAccessPolicy(
                    current,
                    _isDmsAdmin);

            var admin = _access.IsAdministrator;
            SetAdminControlsEnabled(admin);

            if (!admin)
            {
                _users.Clear();
                TxtStatus.Text =
                    T("WLUSERS.Status.AccessDenied");
                return;
            }

            _groups =
                _repository.GetUserGroups();

            CmbGroup.ItemsSource = _groups;

            var allUsers =
                _repository.GetUsers(
                    includeArchived: true);

            _masterUsers = allUsers
                .Where(user =>
                    !user.IsArchived &&
                    !user.IsExternal)
                .OrderBy(user => user.Surname)
                .ThenBy(user => user.FirstName)
                .ToList();

            CmbMasterUser.ItemsSource =
                new WorkLogUser?[] { null }
                    .Concat(_masterUsers)
                    .ToList();

            _users.Clear();

            foreach (var user in allUsers)
            {
                _users.Add(user);
            }

            _usersView =
                CollectionViewSource
                    .GetDefaultView(_users);
            _usersView.Filter = FilterUser;

            WorkLogUser? selected = null;

            if (selectUserId.HasValue)
            {
                selected = _users.FirstOrDefault(
                    user =>
                        user.Id == selectUserId.Value);
            }

            GridUsers.SelectedItem =
                selected ??
                _users.FirstOrDefault(
                    user => !user.IsArchived);

            if (GridUsers.SelectedItem is null)
            {
                ClearEditor();
            }

            TxtStatus.Text = TF(
                "WLUSERS.Status.Loaded",
                _users.Count(
                    user => !user.IsArchived),
                _users.Count(
                    user => user.IsArchived));
        }
        catch (Exception ex)
        {
            SetAdminControlsEnabled(false);
            TxtStatus.Text = TF(
                "WLUSERS.Status.LoadFailed",
                ex.Message);

            _logger?.Error(
                "WLUSERS: load failed.",
                ex);
        }
        finally
        {
            _loading = false;
            _dirty = false;
        }

        if (_access?.IsAdministrator == true &&
            GridUsers.SelectedItem is WorkLogUser selectedUser)
        {
            LoadEditor(selectedUser);
        }
    }

    private bool FilterUser(object item)
    {
        if (item is not WorkLogUser user)
        {
            return false;
        }

        if (ChkShowArchived.IsChecked != true &&
            user.IsArchived)
        {
            return false;
        }

        var filter = TxtFilter.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(user.FirstName, filter) ||
               Contains(user.Surname, filter) ||
               Contains(user.WindowsUsername, filter) ||
               Contains(user.Email, filter) ||
               Contains(user.UserGroupTitle, filter) ||
               user.PersonalNumber
                   .ToString()
                   .Contains(
                       filter,
                       StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(
        string? value,
        string filter) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            filter,
            StringComparison.OrdinalIgnoreCase);

    private void GridUsers_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (_dirty &&
            _selectedUser is not null)
        {
            var discard =
                DmsConfirmDialog.ShowQuestion(
                    Window.GetWindow(this),
                    T("WLUSERS.Dialog.UnsavedTitle"),
                    T("WLUSERS.Dialog.Unsaved"));

            if (!discard)
            {
                _loading = true;
                GridUsers.SelectedItem =
                    _selectedUser;
                _loading = false;
                return;
            }
        }

        if (GridUsers.SelectedItem
            is WorkLogUser user)
        {
            LoadEditor(user);
        }
        else
        {
            ClearEditor();
        }
    }

    private void LoadEditor(WorkLogUser user)
    {
        _loading = true;

        try
        {
            _selectedUser = user;

            TxtFirstName.Text = user.FirstName;
            TxtSurname.Text = user.Surname;
            TxtPersonalNumber.Text =
                user.PersonalNumber.ToString();
            TxtWindowsUsername.Text =
                user.WindowsUsername;
            TxtEmail.Text = user.Email;

            CmbAccess.SelectedValue =
                user.LevelOfAccess;

            CmbUserType.SelectedItem =
                (CmbUserType.ItemsSource
                 as IEnumerable<UserTypeOption>)
                ?.FirstOrDefault(option =>
                    option.IsExternal ==
                    user.IsExternal);

            CmbGroup.SelectedItem =
                _groups.FirstOrDefault(
                    group =>
                        group.Id ==
                        user.UserGroupId);

            CmbMasterUser.SelectedItem =
                _masterUsers.FirstOrDefault(
                    master =>
                        master.Id ==
                        user.MasterUserId);

            BtnArchive.IsEnabled =
                !user.IsArchived;
            BtnRestore.IsEnabled =
                user.IsArchived;

            _dirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ClearEditor()
    {
        _loading = true;

        try
        {
            _selectedUser = null;

            TxtFirstName.Text = string.Empty;
            TxtSurname.Text = string.Empty;
            TxtPersonalNumber.Text = string.Empty;
            TxtWindowsUsername.Text = string.Empty;
            TxtEmail.Text = string.Empty;

            CmbAccess.SelectedValue = 1;
            CmbUserType.SelectedIndex = 0;

            CmbGroup.SelectedItem =
                _groups.FirstOrDefault(
                    group =>
                        !string.Equals(
                            group.Title,
                            "EXTERNISTÉ",
                            StringComparison.OrdinalIgnoreCase))
                ?? _groups.FirstOrDefault();

            CmbMasterUser.SelectedItem = null;

            BtnArchive.IsEnabled = false;
            BtnRestore.IsEnabled = false;

            _dirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetAdminControlsEnabled(bool enabled)
    {
        GridUsers.IsEnabled = enabled;
        TxtFilter.IsEnabled = enabled;
        ChkShowArchived.IsEnabled = enabled;
        BtnReload.IsEnabled = true;

        TxtFirstName.IsEnabled = enabled;
        TxtSurname.IsEnabled = enabled;
        TxtPersonalNumber.IsEnabled = enabled;
        TxtWindowsUsername.IsEnabled = enabled;
        TxtEmail.IsEnabled = enabled;
        CmbAccess.IsEnabled = enabled;
        CmbUserType.IsEnabled = enabled;
        CmbGroup.IsEnabled = enabled;
        CmbMasterUser.IsEnabled = enabled;

        BtnNew.IsEnabled = enabled;
        BtnSave.IsEnabled = enabled;
        BtnArchive.IsEnabled = enabled;
        BtnRestore.IsEnabled = enabled;
    }

    private void Editor_Changed(
        object sender,
        EventArgs e)
    {
        if (!_loading)
        {
            _dirty = true;
        }
    }

    private void CmbUserType_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _dirty = true;

        if (CmbUserType.SelectedItem
            is not UserTypeOption type)
        {
            return;
        }

        if (type.IsExternal)
        {
            CmbGroup.SelectedItem =
                _groups.FirstOrDefault(
                    group =>
                        string.Equals(
                            group.Title,
                            "EXTERNISTÉ",
                            StringComparison.OrdinalIgnoreCase));
        }
        else if (CmbGroup.SelectedItem
                 is WorkLogUserGroup currentGroup &&
                 string.Equals(
                     currentGroup.Title,
                     "EXTERNISTÉ",
                     StringComparison.OrdinalIgnoreCase))
        {
            CmbGroup.SelectedItem =
                _groups.FirstOrDefault(
                    group =>
                        !string.Equals(
                            group.Title,
                            "EXTERNISTÉ",
                            StringComparison.OrdinalIgnoreCase));
        }
    }

    private void TxtFilter_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _usersView?.Refresh();
    }

    private void ChkShowArchived_Changed(
        object sender,
        RoutedEventArgs e)
    {
        _usersView?.Refresh();
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_dirty)
        {
            var discard =
                DmsConfirmDialog.ShowQuestion(
                    Window.GetWindow(this),
                    T("WLUSERS.Dialog.UnsavedTitle"),
                    T("WLUSERS.Dialog.ReloadDiscard"));

            if (!discard)
            {
                return;
            }
        }

        LoadData(_selectedUser?.Id);
    }

    private void BtnNew_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_access?.IsAdministrator != true)
        {
            return;
        }

        if (_dirty)
        {
            var discard =
                DmsConfirmDialog.ShowQuestion(
                    Window.GetWindow(this),
                    T("WLUSERS.Dialog.UnsavedTitle"),
                    T("WLUSERS.Dialog.NewDiscard"));

            if (!discard)
            {
                return;
            }
        }

        _loading = true;
        GridUsers.SelectedItem = null;
        _loading = false;

        ClearEditor();
        TxtFirstName.Focus();

        TxtStatus.Text =
            T("WLUSERS.Status.New");
    }

    private void BtnSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        var repository = _repository;

        if (repository is null ||
            _access?.IsAdministrator != true)
        {
            return;
        }

        var firstName =
            TxtFirstName.Text.Trim();
        var surname =
            TxtSurname.Text.Trim();
        var windowsUsername =
            WorkLogRepository.NormalizeWindowsLogin(
                TxtWindowsUsername.Text);
        var email =
            TxtEmail.Text.Trim();

        if (string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(surname))
        {
            ShowValidation(
                T("WLUSERS.Validation.NameRequired"));
            return;
        }

        if (!int.TryParse(
                TxtPersonalNumber.Text.Trim(),
                out var personalNumber) ||
            personalNumber <= 0)
        {
            ShowValidation(
                T("WLUSERS.Validation.PersonalNumber"));
            return;
        }

        if (string.IsNullOrWhiteSpace(windowsUsername))
        {
            ShowValidation(
                T("WLUSERS.Validation.WindowsUsername"));
            return;
        }

        var duplicate =
            _users.FirstOrDefault(user =>
                user.Id !=
                    (_selectedUser?.Id ?? 0) &&
                string.Equals(
                    WorkLogRepository
                        .NormalizeWindowsLogin(
                            user.WindowsUsername),
                    windowsUsername,
                    StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
        {
            ShowValidation(
                TF(
                    "WLUSERS.Validation.DuplicateWindows",
                    duplicate.DisplayText));
            return;
        }

        if (CmbAccess.SelectedItem
            is not AccessOption accessOption)
        {
            ShowValidation(
                T("WLUSERS.Validation.Access"));
            return;
        }

        if (CmbUserType.SelectedItem
            is not UserTypeOption userType)
        {
            ShowValidation(
                T("WLUSERS.Validation.Type"));
            return;
        }

        var group =
            CmbGroup.SelectedItem
            as WorkLogUserGroup;

        if (userType.IsExternal)
        {
            group = _groups.FirstOrDefault(
                item =>
                    string.Equals(
                        item.Title,
                        "EXTERNISTÉ",
                        StringComparison.OrdinalIgnoreCase));

            if (group is null)
            {
                ShowValidation(
                    T("WLUSERS.Validation.ExternalGroupMissing"));
                return;
            }
        }

        if (group is null)
        {
            ShowValidation(
                T("WLUSERS.Validation.Group"));
            return;
        }

        var master =
            CmbMasterUser.SelectedItem
            as WorkLogUser;

        var oldUser =
            _selectedUser is null
                ? null
                : Clone(_selectedUser);

        var user = new WorkLogUser
        {
            Id = _selectedUser?.Id ?? 0,
            FirstName = firstName,
            Surname = surname,
            PersonalNumber = personalNumber,
            WindowsUsername = windowsUsername,
            LevelOfAccess = accessOption.Value,
            UserGroupId = group.Id,
            UserGroupTitle = group.Title,
            Email = email,
            MasterUserId =
                userType.IsExternal
                    ? master?.Id
                    : null,
            IsArchived =
                _selectedUser?.IsArchived ?? false
        };

        try
        {
            var id = repository.SaveUser(user);

            if (oldUser is null)
            {
                _logger?.AuditCreated(
                    "WORKLOG",
                    "User",
                    id.ToString(),
                    _currentUserName,
                    $"WindowsUsername={windowsUsername}; PersonalNumber={personalNumber}; LevelOfAccess={accessOption.Value}; Group={group.Title}; MasterUserId={user.MasterUserId}");
            }
            else
            {
                LogUserChanges(
                    oldUser,
                    user,
                    id);
            }

            _dirty = false;
            LoadData(id);

            TxtStatus.Text =
                T("WLUSERS.Status.Saved");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLUSERS: user save failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLUSERS.Dialog.ErrorTitle"),
                TF(
                    "WLUSERS.Dialog.SaveFailed",
                    ex.Message));
        }
    }

    private void BtnArchive_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetArchived(true);
    }

    private void BtnRestore_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetArchived(false);
    }

    private void SetArchived(bool archived)
    {
        var repository = _repository;
        var user = _selectedUser;

        if (repository is null ||
            user is null ||
            _access?.IsAdministrator != true)
        {
            return;
        }

        if (user.Id == _access.CurrentUser?.Id &&
            archived)
        {
            ShowValidation(
                T("WLUSERS.Validation.CannotArchiveSelf"));
            return;
        }

        var questionKey =
            archived
                ? "WLUSERS.Dialog.ArchiveQuestion"
                : "WLUSERS.Dialog.RestoreQuestion";

        if (!DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("WLUSERS.Dialog.ArchiveTitle"),
                TF(
                    questionKey,
                    user.DisplayText)))
        {
            return;
        }

        try
        {
            repository.SetUserArchived(
                user.Id,
                archived);

            _logger?.AuditChange(
                "WORKLOG",
                "User",
                user.Id.ToString(),
                "IsArchived",
                user.IsArchived
                    ? "true"
                    : "false",
                archived
                    ? "true"
                    : "false",
                _currentUserName);

            LoadData(
                archived
                    ? null
                    : user.Id);

            TxtStatus.Text =
                archived
                    ? T("WLUSERS.Status.Archived")
                    : T("WLUSERS.Status.Restored");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLUSERS: archive state change failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLUSERS.Dialog.ErrorTitle"),
                ex.Message);
        }
    }

    private void ShowValidation(string message)
    {
        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("WLUSERS.Dialog.ValidationTitle"),
            message);
    }

    private void LogUserChanges(
        WorkLogUser oldUser,
        WorkLogUser newUser,
        int id)
    {
        LogChange(
            id,
            "FirstName",
            oldUser.FirstName,
            newUser.FirstName);
        LogChange(
            id,
            "Surname",
            oldUser.Surname,
            newUser.Surname);
        LogChange(
            id,
            "PersonalNumber",
            oldUser.PersonalNumber.ToString(),
            newUser.PersonalNumber.ToString());
        LogChange(
            id,
            "WindowsUsername",
            oldUser.WindowsUsername,
            newUser.WindowsUsername);
        LogChange(
            id,
            "LevelOfAccess",
            oldUser.LevelOfAccess.ToString(),
            newUser.LevelOfAccess.ToString());
        LogChange(
            id,
            "UserGroupId",
            oldUser.UserGroupId?.ToString(),
            newUser.UserGroupId?.ToString());
        LogChange(
            id,
            "Email",
            oldUser.Email,
            newUser.Email);
        LogChange(
            id,
            "MasterUserID",
            oldUser.MasterUserId?.ToString(),
            newUser.MasterUserId?.ToString());
    }

    private void LogChange(
        int id,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(
                oldValue,
                newValue,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "WORKLOG",
            "User",
            id.ToString(),
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private static WorkLogUser Clone(
        WorkLogUser user)
    {
        return new WorkLogUser
        {
            Id = user.Id,
            FirstName = user.FirstName,
            Surname = user.Surname,
            PersonalNumber = user.PersonalNumber,
            WindowsUsername =
                user.WindowsUsername,
            LevelOfAccess =
                user.LevelOfAccess,
            UserGroupId =
                user.UserGroupId,
            UserGroupTitle =
                user.UserGroupTitle,
            Email = user.Email,
            MasterUserId =
                user.MasterUserId,
            IsArchived =
                user.IsArchived
        };
    }

    private string T(string key)
    {
        if (_translate is null)
        {
            return key;
        }

        var value = _translate(key);

        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(
                   value,
                   $"[[{key}]]",
                   StringComparison.OrdinalIgnoreCase)
            ? key
            : value;
    }

    private string TF(
        string key,
        params object[] args)
    {
        if (_translateFormat is not null)
        {
            return _translateFormat(key, args);
        }

        try
        {
            return string.Format(
                T(key),
                args);
        }
        catch
        {
            return T(key);
        }
    }

    private sealed record AccessOption(
        int Value,
        string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record UserTypeOption(
        bool IsExternal,
        string Text)
    {
        public override string ToString() => Text;
    }
}
