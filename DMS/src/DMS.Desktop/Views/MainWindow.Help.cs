using DMS.Desktop.Views.Help;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderHelp()
    {
        WorkspacePanel.Children.Clear();

        var definitions = GetVisibleTransactionDefinitions();

        WorkspacePanel.Children.Add(new HelpView(
            definitions,
            key => T(key),
            (key, args) => T(key, args),
            executeTransaction: ExecuteTransaction,
            logHelpAction: (action, details) =>
            {
                _logger.AdminAction(
                    "HELP",
                    action,
                    _currentUser.DisplayName,
                    details);
            }));

        _logger.AdminAction(
            "HELP",
            "OpenHelp",
            _currentUser.DisplayName,
            $"VisibleTransactions={definitions.Count}");

        ResetWorkspaceScroll();
    }
}
