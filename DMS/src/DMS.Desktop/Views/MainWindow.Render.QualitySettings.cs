using DMS.Desktop.Views.Quality;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderQualitySettings()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new QualitySettingsView());

        ResetWorkspaceScroll();
    }
}