using DMS.Desktop.Views.Documents;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderArticleDocuments(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var articleFolderPath = Path.Combine(
            _appSettings.DocumentsRootPath,
            "Articles",
            articleNumber);

        var view = new ArticleDocumentsView(
            articleNumber,
            articleFolderPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += transaction => ExecuteTransaction(transaction);

        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "DOC03",
            "OpenArticleDocuments",
            _currentUser.DisplayName,
            $"Article={articleNumber}; Folder={articleFolderPath}");

        ResetWorkspaceScroll();
    }

    private void RenderArticleDocumentEdit(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var articleFolderPath = Path.Combine(
            _appSettings.DocumentsRootPath,
            "Articles",
            articleNumber);

        var view = new ArticleDocumentEditView(
            articleNumber,
            articleFolderPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += transaction => ExecuteTransaction(transaction);

        WorkspacePanel.Children.Add(view);

        _logger.AdminAction(
            "DOC02",
            "OpenArticleDocumentEditor",
            _currentUser.DisplayName,
            $"Article={articleNumber}; Folder={articleFolderPath}");

        ResetWorkspaceScroll();
    }
}
