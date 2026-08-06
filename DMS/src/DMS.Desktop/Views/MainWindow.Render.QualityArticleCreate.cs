using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticleCreate(string query)
    {
        WorkspacePanel.Children.Clear();

        var dmsRootPath = GetDmsDataRootPath();

        _logger.AdminAction(
            "QA01",
            "OpenQualityArticleCreate",
            _currentUser.DisplayName,
            $"Root={dmsRootPath}; Query={query}");

        var view = new QualityArticleCreateView(
            query,
            dmsRootPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
}
