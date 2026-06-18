using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityTasksOverview()
    {
        WorkspacePanel.Children.Clear();

        var view = new QualityTasksOverviewView();
        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);

        ResetWorkspaceScroll();
    }
}