namespace DMS.Desktop.Views;

/// <summary>
/// Thin shell integration for order-related views.
/// ORD10 business/data logic stays outside MainWindow.
/// </summary>
public partial class MainWindow
{
    private void RenderMesOrderHistory(string orderNumber)
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new DMS.Desktop.Views.Orders.MesOrderHistoryView(
            orderNumber, _appSettings.ConfigurationRootPath, _logger,
            _currentUser.DisplayName, key => T(key), _localizationService.ActiveCulture));
        ResetWorkspaceScroll();
    }
    private void RenderOrderOverview()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new DMS.Desktop.Views.Orders.OrderOverviewView(
                _appSettings.ConfigurationRootPath,
                _logger,
                _currentUser.DisplayName,
                openTechnology: sapArticleNumber =>
                    ExecuteTransaction(
                        $"TEC03 {sapArticleNumber}"),
                translate: key => T(key), openMesOrder: code => ExecuteTransaction($"MESORD {code}")));

        ResetWorkspaceScroll();
    }
}
