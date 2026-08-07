using DMS.Desktop.Views.Framework;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkWorkflow()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new FrameworkWorkflowView(
            dataRoot: GetDmsDataRootPath(),
            userRoles: _currentUser.Roles,
            currentUser: _currentUser.DisplayName,
            translate: key => T(key),
            executeTransaction: ExecuteTransaction,
            log: (action, details) => _logger.AdminAction(
                "FW07",
                action,
                _currentUser.DisplayName,
                details)));

        ResetWorkspaceScroll();
    }
}
