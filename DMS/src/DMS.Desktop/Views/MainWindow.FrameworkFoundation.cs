using DMS.Desktop.Views.Framework;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkLocalization()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new FrameworkLocalizationView(
                localizationRoot: Path.Combine(
                    _appSettings.ConfigurationRootPath,
                    "Localization"),
                translate: key => T(key),
                executeTransaction: ExecuteTransaction,
                log: (action, details) =>
                    _logger.AdminAction(
                        "FW01",
                        action,
                        _currentUser.DisplayName,
                        details)));

        ResetWorkspaceScroll();
    }

    private void RenderFrameworkUiStandards()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new FrameworkUiStandardsView(
                translate: key => T(key),
                executeTransaction: ExecuteTransaction,
                log: (action, details) =>
                    _logger.AdminAction(
                        "FW02",
                        action,
                        _currentUser.DisplayName,
                        details)));

        ResetWorkspaceScroll();
    }
}
