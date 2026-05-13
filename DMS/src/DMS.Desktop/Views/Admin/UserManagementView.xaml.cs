using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DMS.Core.Security;

namespace DMS.Desktop.Views.Admin;

public partial class UserManagementView : UserControl
{
    private readonly string _usersFilePath;
    private readonly DmsUserContext _currentUser;

    private readonly List<string> _availableRoles = new()
    {
        "DMS_ADMIN",
        "DMS_TECHNOLOGIE",
        "DMS_KVALITA",
        "DMS_VYROBA",
        "DMS_READONLY"
    };

    private List<DmsUser> _users = new();

    public UserManagementView(string usersFilePath, DmsUserContext currentUser)
    {
        InitializeComponent();

        _usersFilePath = usersFilePath;
        _currentUser = currentUser;

        BuildRoleCheckboxes();
        LoadUsers();
    }

    private void BuildRoleCheckboxes()
    {
        RolePanel.Children.Clear();

        foreach (var role in _availableRoles)
        {
            RolePanel.Children.Add(new CheckBox
            {
                Content = role,
                Tag = role,
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
    }

    private void LoadUsers()
    {
        if (!File.Exists(_usersFilePath))
        {
            _users = new List<DmsUser>();
            RefreshUserList();
            return;
        }

        var json = File.ReadAllText(_usersFilePath);

        _users = JsonSerializer.Deserialize<List<DmsUser>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<DmsUser>();

        RefreshUserList();
    }

    private void RefreshUserList()
    {
        LstUsers.ItemsSource = null;
        LstUsers.ItemsSource = _users
            .OrderBy(user => user.DisplayName)
            .ToList();
    }

    private void LstUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstUsers.SelectedItem is not DmsUser user)
        {
            return;
        }

        ShowUser(user);
    }

    private void ShowUser(DmsUser user)
    {
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

            var role = checkBox.Tag?.ToString() ?? string.Empty;

            checkBox.IsChecked = user.Roles.Any(userRole =>
                string.Equals(userRole, role, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        LstUsers.SelectedItem = null;

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

        TxtWindowsLogin.Focus();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        LoadUsers();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var windowsLogin = TxtWindowsLogin.Text.Trim();
        var displayName = TxtDisplayName.Text.Trim();
        var email = TxtEmail.Text.Trim();

        if (string.IsNullOrWhiteSpace(windowsLogin))
        {
            MessageBox.Show(
                "Windows login je povinný.",
                "DMS - správa uživatelů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

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
            MessageBox.Show(
                "Uživatel musí mít alespoň jednu roli.",
                "DMS - správa uživatelů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var existingIndex = _users.FindIndex(user =>
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

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
        RefreshUserList();

        MessageBox.Show(
            $"Uživatel {displayName} byl uložen.",
            "DMS - správa uživatelů",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
            MessageBox.Show(
                "Nemůžeš smazat aktuálně přihlášeného uživatele.",
                "DMS - správa uživatelů",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var result = MessageBox.Show(
            $"Opravdu chceš smazat uživatele?\n\n{windowsLogin}",
            "DMS - správa uživatelů",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _users.RemoveAll(user =>
            string.Equals(user.WindowsLogin, windowsLogin, StringComparison.OrdinalIgnoreCase));

        SaveUsers();
        RefreshUserList();
        BtnNew_Click(sender, e);
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
                roles.Add(role);
            }
        }

        return roles;
    }

    private void SaveUsers()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_usersFilePath)!);

        var json = JsonSerializer.Serialize(
            _users.OrderBy(user => user.DisplayName).ToList(),
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(_usersFilePath, json);
    }
}