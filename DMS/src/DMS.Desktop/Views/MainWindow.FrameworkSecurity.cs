using DMS.Desktop.Views.Framework;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkSecurity()
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new FrameworkSecurityView(
            configRoot: _appSettings.ConfigurationRootPath,
            currentWindowsLogin: _currentUser.WindowsLogin,
            translate: key => T(key),
            executeTransaction: ExecuteTransaction,
            log: (action, details) => _logger.AdminAction(
                "FW06",
                action,
                _currentUser.DisplayName,
                details)));
        ResetWorkspaceScroll();

        _logger.AdminAction(
            "FW06",
            "SECURITY_FRAMEWORK_OPEN",
            _currentUser.DisplayName,
            $"WindowsLogin={_currentUser.WindowsLogin}; Roles={string.Join(",", _currentUser.Roles)}");
    }
}
