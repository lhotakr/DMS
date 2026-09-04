using DMS.Desktop.UI;
using DMS.Desktop.Views.Quality;
using System.Linq;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticleEdit(string query)
    {
        if (!CanLeaveCurrentWorkspace())
        {
            return;
        }

        WorkspacePanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            RenderSimplePage(
                T("QA02.Title"),
                T("QA02.Warning.MissingQuery"));

            return;
        }

        var dmsRootPath = GetDmsDataRootPath();

        _logger.AdminAction(
            "QA02",
            "OpenQualityArticleEdit",
            _currentUser.DisplayName,
            $"Root={dmsRootPath}; Query={query}");

        var view = new QualityArticleEditView(
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

    private bool CanLeaveCurrentWorkspace()
    {
        var guard = WorkspacePanel.Children
            .OfType<IUnsavedChangesGuard>()
            .FirstOrDefault();

        return guard?.ConfirmNavigationAway() ?? true;
    }
}
