using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityPrintVersions()
    {
        WorkspacePanel.Children.Clear();

        var rootPath = GetDmsDataRootPath();

        var view = new QualityPrintVersionListView(
            rootPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QA05",
            "OpenPrintVersionOverview",
            _currentUser.DisplayName,
            $"Root={rootPath}");

        ResetWorkspaceScroll();
    }
}
