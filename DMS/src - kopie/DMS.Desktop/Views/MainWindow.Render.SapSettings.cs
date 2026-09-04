using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSapSettings()
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        _logger.AdminAction(
            "SAPSET",
            "OpenSapSettings",
            _currentUser.DisplayName,
            $"Root={storagePaths.RootDirectory}; Config={storagePaths.ConfigDirectory}");

        WorkspacePanel.Children.Add(new SapSettingsView(
            storagePaths,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }
}