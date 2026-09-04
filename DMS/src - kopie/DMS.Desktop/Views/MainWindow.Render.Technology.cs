using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderTechnicalArticleSummary(string articleNumber)
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        _logger.AdminAction(
            "TEC03",
            "OpenTechnicalSummary",
            _currentUser.DisplayName,
            $"ArticleNumber={articleNumber}; Root={storagePaths.RootDirectory}; " +
            $"Boms={storagePaths.SapBomSnapshotsFilePath}; " +
            $"Routings={storagePaths.SapRoutingSnapshotsFilePath}");

        WorkspacePanel.Children.Add(new TechnicalArticleSummaryView(
            articleNumber,
            storagePaths,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }
}