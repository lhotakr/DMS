using DMS.Desktop.Views.Checklists;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderChecklistWorkspace(string transactionCode, IReadOnlyList<string> arguments)
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new ChecklistWorkspaceView(
            transactionCode,
            arguments,
            GetDmsDataRootPath(),
            _currentUser.WindowsLogin,
            _currentUser.DisplayName,
            _currentUser.PersonId,
            _currentUser.Roles,
            executeTransaction: ExecuteTransaction,
            audit: (action, details) => _logger.AdminAction(
                transactionCode,
                action,
                _currentUser.DisplayName,
                details),
            translate: key => T(key)));

        ResetWorkspaceScroll();
    }
    private void RenderChecklistSettings()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new ChecklistSettingsView(
            GetDmsDataRootPath(),
            audit: (action, details) => _logger.AdminAction(
                "CHLSET",
                action,
                _currentUser.DisplayName,
                details)));

        ResetWorkspaceScroll();
    }

}
