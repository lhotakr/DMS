namespace DMS.Desktop.Views;

/// <summary>
/// Thin shell wrapper for the MES live production overview.
/// Business/data logic remains outside MainWindow.
/// </summary>
public partial class MainWindow
{
    private void RenderMesLiveOverview()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new DMS.Desktop.Views.Mes.MesLiveOverviewView(
                _appSettings.ConfigurationRootPath,
                _logger,
                _currentUser.DisplayName,
                translate: key => T(key)));

        ResetWorkspaceScroll();
    }
}
