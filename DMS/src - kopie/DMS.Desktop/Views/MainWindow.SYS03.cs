using DMS.Core.Sap;
using DMS.Desktop.Views.SystemOverview;
using System.IO;
using System.Windows.Controls;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSystemConfiguration()
    {
        WorkspacePanel.Children.Clear();

        var localizationRootPath = Path.Combine(
            _appSettings.ConfigurationRootPath,
            "Localization");

        var sapMaterialsFilePath = new SapStoragePaths(GetDmsDataRootPath())
            .SapMaterialsFilePath;

        WorkspacePanel.Children.Add(new Sys03SystemOverviewView(
            _appSettings,
            _systemSettings,
            _systemSettingsPath,
            sapMaterialsFilePath,
            localizationRootPath,
            GetVisibleTransactionDefinitions(),
            _currentUser,
            key => T(key),
            (action, details) =>
            {
                _logger.AdminAction(
                    "SYS03",
                    action,
                    _currentUser.DisplayName,
                    details);
            }));

        ResetWorkspaceScroll();
    }
}
