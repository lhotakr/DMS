using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityArticleCreate(string query)
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityArticleCreateView(query);
        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
}