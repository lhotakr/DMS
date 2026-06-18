using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSapSettings()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new SapSettingsView());

        ResetWorkspaceScroll();
    }
}