using DMS.Desktop.Views.Framework;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkMasterData()
    {
        WorkspacePanel.Children.Clear();

        var masterDataRoot =
            Path.Combine(
                GetDmsDataRootPath(),
                "Data",
                "MasterData");

        WorkspacePanel.Children.Add(
            new FrameworkMasterDataView(
                masterDataRoot: masterDataRoot,
                usersPath: _usersConfigPath,
                translate: key => T(key),
                executeTransaction: ExecuteTransaction,
                log: (action, details) =>
                    _logger.AdminAction(
                        "FW09",
                        action,
                        _currentUser.DisplayName,
                        details)));

        ResetWorkspaceScroll();
    }
}
