using DMS.Desktop.Views.Documents;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderArticleDocumentCreate(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var articleFolderPath = Path.Combine(
            _appSettings.DocumentsRootPath,
            "Articles",
            articleNumber);

        var view = new ArticleDocumentCreateView(
            articleNumber,
            GetDmsDataRootPath(),
            articleFolderPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += transaction => ExecuteTransaction(transaction);

        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "DOC01",
            "OpenArticleDocumentationInitialization",
            _currentUser.DisplayName,
            $"Article={articleNumber}; Folder={articleFolderPath}; Root={GetDmsDataRootPath()}");

        ResetWorkspaceScroll();
    }
}
