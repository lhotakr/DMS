using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSapCacheStatus()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(new SapCacheStatusView());

        ResetWorkspaceScroll();
    }
}