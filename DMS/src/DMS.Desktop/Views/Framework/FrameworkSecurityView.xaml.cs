using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DMS.Core.Security;
using DMS.Core.Transactions;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkSecurityView : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configRoot;
    private readonly string _currentWindowsLogin;
    private readonly Func<string, string> _translate;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string> _log;
    private readonly DmsAuthorizationService _authorization = new();

    private List<SecurityUserRow> _users = new();
    private List<SecurityRoleRow> _roles = new();
    private List<TransactionDefinition> _transactions = new();

    public FrameworkSecurityView(
        string configRoot,
        string currentWindowsLogin,
        Func<string, string> translate,
        Action<string> executeTransaction,
        Action<string, string> log)
    {
        InitializeComponent();

        _configRoot = configRoot;
        _currentWindowsLogin = currentWindowsLogin;
        _translate = translate;
        _executeTransaction = executeTransaction;
        _log = log;

        ApplyLocalization();
        Loaded += (_, _) => Reload();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TitleText.Text = T("Framework.FW06.Title", "FW06 — Security and permissions");
        SubtitleText.Text = T(
            "Framework.FW06.Description",
            "Shows effective transaction permissions for a selected user and detects broken role references.");

        ReloadButton.Content = T("Framework.FW06.Reload", "Reload");
        UsersButton.Content = T("Framework.FW06.Users", "Open USR01");
        RolesButton.Content = T("Framework.FW06.Roles", "Open SYS12");
        TransactionsButton.Content = T("Framework.FW06.Transactions", "Open SYS11");
        UserLabel.Text = T("Framework.FW06.User", "User");
        SummaryLabel.Text = T("Framework.FW06.SummaryLabel", "Effective access");

        CodeColumn.Header = T("Framework.FW06.Column.Code", "Transaction");
        ModuleColumn.Header = T("Framework.FW06.Column.Module", "Module");
        NameColumn.Header = T("Framework.FW06.Column.Name", "Name");
        RequiredColumn.Header = T("Framework.FW06.Column.Required", "Required roles");
        AllowedColumn.Header = T("Framework.FW06.Column.Allowed", "Allowed");
        ReasonColumn.Header = T("Framework.FW06.Column.Reason", "Reason");

        SeverityColumn.Header = T("Framework.FW06.Column.Severity", "Severity");
        CheckColumn.Header = T("Framework.FW06.Column.Check", "Security check");
        DetailsColumn.Header = T("Framework.FW06.Column.Details", "Details");

        FooterText.Text = T(
            "Framework.FW06.Footer",
            "FW06 is read-only. Change users in USR01, role definitions in SYS12 and transaction role assignments in SYS11.");
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => Reload();
    private void UsersButton_Click(object sender, RoutedEventArgs e) => _executeTransaction("USR01");
    private void RolesButton_Click(object sender, RoutedEventArgs e) => _executeTransaction("SYS12");
    private void TransactionsButton_Click(object sender, RoutedEventArgs e) => _executeTransaction("SYS11");

    private void UserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectedUser();
    }

    private void Reload()
    {
        _roles = LoadRoles();
        _users = LoadUsers();
        _transactions = LoadTransactions();

        UserCombo.ItemsSource = _users;

        var selected =
            _users.FirstOrDefault(x =>
                string.Equals(
                    x.WindowsLogin,
                    _currentWindowsLogin,
                    StringComparison.OrdinalIgnoreCase))
            ?? _users.FirstOrDefault();

        if (selected is not null)
        {
            UserCombo.SelectedItem = selected;
        }

        RefreshDiagnostics();
        RefreshSelectedUser();

        _log(
            "SECURITY_OVERVIEW",
            $"Users={_users.Count}; Roles={_roles.Count}; Transactions={_transactions.Count}");
    }

    private void RefreshSelectedUser()
    {
        if (UserCombo.SelectedItem is not SecurityUserRow user)
        {
            PermissionGrid.ItemsSource = null;
            SummaryText.Text = string.Empty;
            return;
        }

        var context = new DmsUserContext
        {
            WindowsLogin = user.WindowsLogin,
            DisplayName = user.DisplayName,
            PersonId = user.PersonId,
            Roles = user.Roles
        };

        var rows = _transactions
            .OrderBy(x => x.Module)
            .ThenBy(x => x.Code)
            .Select(definition =>
            {
                var result = _authorization.EvaluateTransaction(context, definition);
                return new PermissionRow(
                    definition.Code,
                    definition.Module,
                    definition.Name,
                    definition.Roles.Count == 0
                        ? T("Framework.FW06.Public", "No role required")
                        : string.Join(", ", definition.Roles),
                    result.Allowed,
                    DescribeResult(result));
            })
            .ToList();

        PermissionGrid.ItemsSource = rows;

        var allowed = rows.Count(x => x.Allowed);
        var denied = rows.Count - allowed;
        SummaryText.Text = string.Format(
            T(
                "Framework.FW06.Summary",
                "{0} allowed / {1} denied | Roles: {2}"),
            allowed,
            denied,
            user.Roles.Count == 0
                ? T("Framework.FW06.NoRoles", "none")
                : string.Join(", ", user.Roles));
    }

    private string DescribeResult(DmsAuthorizationResult result) =>
        result.ReasonCode switch
        {
            "NO_ROLE_REQUIRED" => T("Framework.FW06.Reason.Public", "Public for signed-in users"),
            "ROLE_MATCH" => string.Format(
                T("Framework.FW06.Reason.RoleMatch", "Matched: {0}"),
                string.Join(", ", result.MatchingRoles)),
            "ROLE_REQUIRED" => T("Framework.FW06.Reason.RoleRequired", "Required role is missing"),
            "TRANSACTION_INACTIVE" => T("Framework.FW06.Reason.Inactive", "Transaction is inactive"),
            _ => result.ReasonCode
        };

    private void RefreshDiagnostics()
    {
        var diagnostics = new List<SecurityDiagnosticRow>();

        var knownRoles = _roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToDictionary(
                x => x.Code,
                StringComparer.OrdinalIgnoreCase);

        var duplicateRoles = _roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        AddDiagnostic(
            diagnostics,
            duplicateRoles.Count == 0 ? "OK" : "ERROR",
            T("Framework.FW06.Check.DuplicateRoles", "Duplicate role codes"),
            duplicateRoles.Count == 0
                ? T("Framework.FW06.NoProblem", "No problem found.")
                : string.Join(", ", duplicateRoles));

        var unknownTransactionRoles = _transactions
            .SelectMany(transaction => transaction.Roles.Select(role => (transaction.Code, Role: role)))
            .Where(x => !knownRoles.ContainsKey(x.Role))
            .Select(x => $"{x.Code}->{x.Role}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        AddDiagnostic(
            diagnostics,
            unknownTransactionRoles.Count == 0 ? "OK" : "ERROR",
            T("Framework.FW06.Check.TransactionRoles", "Transaction role references"),
            unknownTransactionRoles.Count == 0
                ? T("Framework.FW06.NoProblem", "No problem found.")
                : string.Join(", ", unknownTransactionRoles.Take(30)));

        var unknownUserRoles = _users
            .SelectMany(user => user.Roles.Select(role => (user.WindowsLogin, Role: role)))
            .Where(x => !knownRoles.ContainsKey(x.Role))
            .Select(x => $"{x.WindowsLogin}->{x.Role}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        AddDiagnostic(
            diagnostics,
            unknownUserRoles.Count == 0 ? "OK" : "ERROR",
            T("Framework.FW06.Check.UserRoles", "User role references"),
            unknownUserRoles.Count == 0
                ? T("Framework.FW06.NoProblem", "No problem found.")
                : string.Join(", ", unknownUserRoles.Take(30)));

        var inactiveAssigned = _users
            .SelectMany(user => user.Roles.Select(role => (user.WindowsLogin, Role: role)))
            .Where(x =>
                knownRoles.TryGetValue(x.Role, out var role) &&
                !role.IsActive)
            .Select(x => $"{x.WindowsLogin}->{x.Role}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        AddDiagnostic(
            diagnostics,
            inactiveAssigned.Count == 0 ? "OK" : "WARNING",
            T("Framework.FW06.Check.InactiveAssigned", "Inactive roles assigned to users"),
            inactiveAssigned.Count == 0
                ? T("Framework.FW06.NoProblem", "No problem found.")
                : string.Join(", ", inactiveAssigned.Take(30)));

        var publicTransactions = _transactions
            .Where(x => x.IsActive && x.Roles.Count == 0)
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        AddDiagnostic(
            diagnostics,
            publicTransactions.Count == 0 ? "OK" : "WARNING",
            T("Framework.FW06.Check.PublicTransactions", "Transactions without role restriction"),
            publicTransactions.Count == 0
                ? T("Framework.FW06.None", "None.")
                : string.Join(", ", publicTransactions.Take(50)));

        var inactiveUsersWithRoles = _users
            .Where(x => !x.IsActive && x.Roles.Count > 0)
            .Select(x => x.WindowsLogin)
            .OrderBy(x => x)
            .ToList();

        AddDiagnostic(
            diagnostics,
            inactiveUsersWithRoles.Count == 0 ? "OK" : "WARNING",
            T("Framework.FW06.Check.InactiveUsers", "Inactive users still holding roles"),
            inactiveUsersWithRoles.Count == 0
                ? T("Framework.FW06.NoProblem", "No problem found.")
                : string.Join(", ", inactiveUsersWithRoles.Take(30)));

        DiagnosticsGrid.ItemsSource = diagnostics;
    }

    private static void AddDiagnostic(
        ICollection<SecurityDiagnosticRow> rows,
        string severity,
        string check,
        string details) =>
        rows.Add(new SecurityDiagnosticRow(severity, check, details));

    private List<SecurityRoleRow> LoadRoles()
    {
        var path = Path.Combine(_configRoot, "dms-roles.json");
        if (!File.Exists(path))
        {
            return new List<SecurityRoleRow>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<SecurityRoleRow>>(
                       File.ReadAllText(path),
                       JsonOptions)
                   ?? new List<SecurityRoleRow>();
        }
        catch
        {
            return new List<SecurityRoleRow>();
        }
    }

    private List<SecurityUserRow> LoadUsers()
    {
        var path = Path.Combine(_configRoot, "users.json");
        if (!File.Exists(path))
        {
            return new List<SecurityUserRow>();
        }

        try
        {
            var users = JsonSerializer.Deserialize<List<SecurityUserRow>>(
                            File.ReadAllText(path),
                            JsonOptions)
                        ?? new List<SecurityUserRow>();

            foreach (var user in users)
            {
                user.Roles = (user.Roles ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            return users
                .OrderByDescending(x =>
                    string.Equals(
                        x.WindowsLogin,
                        _currentWindowsLogin,
                        StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.DisplayName)
                .ThenBy(x => x.WindowsLogin)
                .ToList();
        }
        catch
        {
            return new List<SecurityUserRow>();
        }
    }

    private List<TransactionDefinition> LoadTransactions()
    {
        var path = Path.Combine(_configRoot, "transactions.json");
        if (!File.Exists(path))
        {
            return new List<TransactionDefinition>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<TransactionDefinition>>(
                       File.ReadAllText(path),
                       JsonOptions)
                   ?? new List<TransactionDefinition>();
        }
        catch
        {
            return new List<TransactionDefinition>();
        }
    }

    private sealed class SecurityUserRow
    {
        public string WindowsLogin { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public Guid? PersonId { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> Roles { get; set; } = new();

        public string DisplayText =>
            string.IsNullOrWhiteSpace(DisplayName)
                ? WindowsLogin
                : $"{DisplayName} — {WindowsLogin}";
    }

    private sealed class SecurityRoleRow
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    private sealed record PermissionRow(
        string Code,
        string Module,
        string Name,
        string RequiredRoles,
        bool Allowed,
        string Reason);

    private sealed record SecurityDiagnosticRow(
        string Severity,
        string Check,
        string Details);
}
