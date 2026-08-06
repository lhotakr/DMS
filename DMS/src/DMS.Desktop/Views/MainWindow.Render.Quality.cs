using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityCockpit()
    {
        WorkspacePanel.Children.Clear();

        var dmsRootPath = GetDmsDataRootPath();

        _logger.AdminAction(
            "QA00",
            "OpenQualityCockpit",
            _currentUser.DisplayName,
            $"Root={dmsRootPath}");

        WorkspacePanel.Children.Add(new QualityCockpitView(
            dmsRootPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }
}
