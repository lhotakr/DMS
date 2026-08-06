using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualitySettings()
    {
        WorkspacePanel.Children.Clear();

        var dmsRootPath = GetDmsDataRootPath();

        _logger.AdminAction(
            "QASET",
            "OpenQualitySettings",
            _currentUser.DisplayName,
            $"Root={dmsRootPath}");

        WorkspacePanel.Children.Add(new QualitySettingsView(
            dmsRootPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }
}
