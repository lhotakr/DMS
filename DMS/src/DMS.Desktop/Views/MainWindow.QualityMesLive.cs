using DMS.Desktop.Views.QualityOrders;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void ShowQaMesLivePanel(string qaOrderNumber)
    {
        WorkspacePanel.Children.Insert(0, QualityMesLivePanel.Create(
            qaOrderNumber, GetDmsDataRootPath(),
            _appSettings.ConfigurationRootPath, _logger,
            _currentUser.DisplayName, key => T(key)));
    }
}
