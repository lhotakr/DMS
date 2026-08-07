using DMS.Desktop.Logging;
using DMS.Desktop.Views.Framework;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkAuditLogging()
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new FrameworkAuditLoggingView(
            logger: _logger,
            reader: _logReader,
            currentUser: _currentUser.DisplayName,
            translate: key => T(key),
            executeTransaction: ExecuteTransaction));
        ResetWorkspaceScroll();

        _logger.FrameworkEvent(
            DmsAuditEventNames.FrameworkDiagnostic,
            new DmsAuditContext
            {
                TransactionCode = "FW05",
                ModuleCode = "ADMIN",
                Area = "Framework",
                Entity = "LoggingDashboard",
                User = _currentUser.DisplayName
            },
            "OPEN",
            $"LogsRootPath={_appSettings.LogsRootPath}");
    }
}
