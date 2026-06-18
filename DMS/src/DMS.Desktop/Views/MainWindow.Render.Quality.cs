using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualityCockpit()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new QualityCockpitView());

        ResetWorkspaceScroll();
    }
}