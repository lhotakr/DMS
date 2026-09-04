using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSapCacheStatus()
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        WorkspacePanel.Children.Add(new SapCacheStatusView(
            storagePaths.RootDirectory,
            storagePaths.ConfigDirectory,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args),
            logAction: (action, details) =>
            {
                _logger.AdminAction(
                    "SAP00",
                    action,
                    _currentUser.DisplayName,
                    details);
            }));

        ResetWorkspaceScroll();
    }
}
