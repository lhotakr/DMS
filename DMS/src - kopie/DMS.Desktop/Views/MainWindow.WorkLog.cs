using DMS.Desktop.Views.WorkLog;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private bool IsCurrentUserDmsAdmin() =>
        _currentUser.Roles.Any(
            role =>
                string.Equals(
                    role,
                    "DMS_ADMIN",
                    StringComparison.OrdinalIgnoreCase));

    private void RenderWorkLogDashboard()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new WorkLogDashboardView(
                _appSettings.ConfigurationRootPath,
                _currentUser.WindowsLogin,
                _currentUser.DisplayName,
                IsCurrentUserDmsAdmin(),
                _logger,
                translate: key => T(key),
                translateFormat:
                    (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderWorkLogUsers()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new WorkLogUsersView(
                _appSettings.ConfigurationRootPath,
                _currentUser.WindowsLogin,
                _currentUser.DisplayName,
                IsCurrentUserDmsAdmin(),
                _logger,
                translate: key => T(key),
                translateFormat:
                    (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderWorkLogWork()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new WorkLogWorkView(
                _appSettings.ConfigurationRootPath,
                _currentUser.WindowsLogin,
                _currentUser.DisplayName,
                IsCurrentUserDmsAdmin(),
                _logger,
                translate: key => T(key),
                translateFormat:
                    (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderWorkLogLock()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new WorkLogLockView(
                _appSettings.ConfigurationRootPath,
                _currentUser.WindowsLogin,
                _currentUser.DisplayName,
                IsCurrentUserDmsAdmin(),
                _logger,
                translate: key => T(key),
                translateFormat:
                    (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderWorkLogConfig()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new WorkLogConfigView(
                _appSettings.ConfigurationRootPath,
                _currentUser.WindowsLogin,
                _currentUser.DisplayName,
                IsCurrentUserDmsAdmin(),
                _logger,
                translate: key => T(key),
                translateFormat:
                    (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }
}
