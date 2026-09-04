using DMS.Desktop.Performance;
using DMS.Desktop.Views.Framework;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkPerformance()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new FrameworkPerformanceView(
                performance: DmsPerformanceService.Current,
                configRoot: _appSettings.ConfigurationRootPath,
                dataRoot: GetDmsDataRootPath(),
                articlesDataPath: _appSettings.ArticlesDataPath,
                currentUser: _currentUser.DisplayName,
                translate: key => T(key),
                executeTransaction: ExecuteTransaction,
                log: (action, details) => _logger.AdminAction(
                    "FW08",
                    action,
                    _currentUser.DisplayName,
                    details)));

        ResetWorkspaceScroll();
    }
}
