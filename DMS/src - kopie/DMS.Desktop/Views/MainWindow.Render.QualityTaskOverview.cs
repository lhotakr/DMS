using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityTasksOverview()
    {
        WorkspacePanel.Children.Clear();

        var dmsRootPath = GetDmsDataRootPath();

        var view = new QualityTasksOverviewView(
            dmsRootPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QATASK",
            "OpenQualityTasksOverview",
            _currentUser.DisplayName,
            $"Root={dmsRootPath}");

        ResetWorkspaceScroll();
    }
}
