using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticle(string query)
    {
        WorkspacePanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            RenderSimplePage(
                T("QA03.Empty.Title"),
                T("QA03.Empty.Body"));
            return;
        }

        var view = new QualityArticleView(
            query,
            GetDmsDataRootPath(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "QA03",
            "OpenQualityArticleView",
            _currentUser.DisplayName,
            $"Query={query}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }
}
