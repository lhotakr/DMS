using DMS.Core.Security;
using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.UI;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Admin;

public partial class UserManagementView : UserControl, IUnsavedChangesGuard
{
    private readonly string _usersFilePath;
    private readonly string _rolesFilePath;
    private readonly DmsUserContext _currentUser;
    private readonly DmsRoleManagementService _roleService;

    private List<DmsRoleDefinition> _availableRoles = new();
    private List<DmsUser> _users = new();

    private bool _isLoadingUser;
    private bool _suppressSelectionChanged;
    private bool _hasUnsavedChanges;
    private string? _selectedWindowsLogin;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public UserManagementView(
        string usersFilePath,
        DmsUserContext currentUser)
        : this(
            usersFilePath,
            Path.Combine(AppContext.BaseDirectory, "Config", "dms-roles.json"),
            currentUser)
    {
    }

    public UserManagementView(
        string usersFilePath,
        string rolesFilePath,
        DmsUserContext currentUser)
    {
        InitializeComponent();

        _usersFilePath = usersFilePath;
        _rolesFilePath = rolesFilePath;
        _currentUser = currentUser;
        _roleService = new DmsRoleManagementService(_rolesFilePath);

        LoadRoles();
        LoadUsers();
        ClearEditor();
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            "USR01 - Neuložené změny",
            "Ve správě uživatelů jsou neuložené změny.\n\nChceš opravdu pokračovat bez uložení?");
    }

    private void LoadRoles()
    {
        _availableRoles = _roleService.LoadAll()
            .Where(role => role.IsActive)
            .OrderBy(role => role.Code)
            .ToList();

        BuildRoleCheckboxes();

        TxtStatus.Text = $"Načteno rolí: {_availableRoles.Count}";
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
                Foreground = TryFindResource("DmsForegroundBrush") as System.Windows.Media.Brush
            });

            if (!string.IsNullOrWhiteSpace(role.Name))
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = role.Name,
                    Foreground = TryFindResource("DmsMutedForegroundBrush") as System.Windows.Media.Brush
                });
            }

            if (!string.IsNullOrWhiteSpace(role.Description))
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = role.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TryFindResource("DmsMutedForegroundBrush") as System.Windows.Media.Brush
                });
            }

            checkBox.Content = contentPanel;
            checkBox.Checked += EditorChanged;
            checkBox.Unchecked += EditorChanged;

            RolePanel.Children.Add(checkBox);
        }
    }

    private void LoadUsers()
    {
        if (!File.Exists(_usersFilePath))
        {
            _users = new List<DmsUser>();
            RefreshUserList();
            TxtStatus.Text = "Soubor uživatelů zatím neexistuje.";
            return;
        }

        try
        {
            var json = File.ReadAllText(_usersFilePath);

            _users = JsonSerializer.Deserialize<List<DmsUser>>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<DmsUser>();

            RefreshUserList();

            TxtStatus.Text = $"Načteno uživatelů: {_users.Count}";
        }
        catch (Exception ex)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "USR01 - Chyba načtení",
                $"Načtení uživatelů selhalo:\n\n{ex.Message}");

            _users = new List<DmsUser>();
            RefreshUserList();
        }
    }

    private void RefreshUserList()
    {
        var previouslySelectedLogin = _selectedWindowsLogin;

        LstUsers.ItemsSource = null;
        LstUsers.ItemsSource = _users
            .OrderBy(user => user.DisplayName)
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
                "USR01 - Neuložené změny",
                "Aktuální uživatel má neuložené změny.\n\nChceš je zahodit a přejít na jiného uživatele?");

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
            TxtStatus.Text = $"Vybrán uživatel: {user.DisplayName}";
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
                "USR01 - Nový uživatel",
                "Aktuální detail má neuložené změny.\n\nChceš je zahodit a založit nového uživatele?");

            if (!confirm)
            {
                return;
            }
        }

        _suppressSelectionChanged = true;
        LstUsers.SelectedItem = null;
        _suppressSelectionChanged = false;

        ClearEditor();

        TxtStatus.Text = "Nový uživatel.";
        TxtWindowsLogin.Focus();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges)
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "USR01 - Znovu načíst",
                "Ve správě uživatelů jsou neuložené změny.\n\nChceš je zahodit a znovu načíst uživatele i role?");

            if (!confirm)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "USR01 - Znovu načíst",
                "Chceš znovu načíst uživatele i role?");

            if (!confirm)
            {
                return;
            }
        }

        LoadRoles();
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
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "USR01 - Kontrola uživatele",
                "Windows login je povinný.");

            TxtWindowsLogin.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = windowsLogin;
        }

        var selectedRoles = GetSelectedRoles();

        if (selectedRoles.Count == 0)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "USR01 - Kontrola uživatele",
                "Uživatel musí mít alespoň jednu roli.");

            return;
        }

        var duplicateLogin = _users.Any(user =>
            !string.Equals(user.WindowsLogin, _selectedWindowsLogin, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        if (duplicateLogin)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "USR01 - Kontrola uživatele",
                $"Uživatel s Windows loginem už existuje:\n\n{windowsLogin}");

            return;
        }

        var existingIndex = _users.FindIndex(user =>
            string.Equals(user.WindowsLogin, _selectedWindowsLogin ?? windowsLogin, StringComparison.OrdinalIgnoreCase));

        var savedUser = new DmsUser
        {
            WindowsLogin = windowsLogin,
            DisplayName = displayName,
            Email = email,
            IsActive = ChkIsActive.IsChecked == true,
            Roles = selectedRoles
        };

        if (existingIndex >= 0)
        {
            _users[existingIndex] = savedUser;
        }
        else
        {
            _users.Add(savedUser);
        }

        SaveUsers();

        _selectedWindowsLogin = savedUser.WindowsLogin;
        _hasUnsavedChanges = false;

        RefreshUserList();
        ShowUser(savedUser);

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            "USR01",
            $"Uživatel {displayName} byl uložen.");
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
                "USR01 - Správa uživatelů",
                "Nemůžeš smazat aktuálně přihlášeného uživatele.");

            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            "USR01 - Smazat uživatele",
            $"Opravdu chceš smazat uživatele?\n\n{windowsLogin}");

        if (!confirm)
        {
            return;
        }

        _users.RemoveAll(user =>
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        SaveUsers();
        RefreshUserList();
        ClearEditor();

        TxtStatus.Text = $"Uživatel {windowsLogin} byl smazán.";
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
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(_usersFilePath, json);
    }

    private void EditorChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUser)
        {
            return;
        }

        _hasUnsavedChanges = true;

        if (TxtStatus is not null)
        {
            TxtStatus.Text = "Detail uživatele obsahuje neuložené změny.";
        }
    }
}