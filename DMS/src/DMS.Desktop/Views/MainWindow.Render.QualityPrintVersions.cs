using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityPrintVersions()
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityPrintVersionListView();

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
}