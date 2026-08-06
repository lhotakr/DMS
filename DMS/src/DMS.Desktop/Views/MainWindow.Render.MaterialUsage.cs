using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderMaterialUsage(string materialNumber)
    {
        WorkspacePanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(materialNumber))
        {
            RenderSimplePage(T("MAT03.Title"), T("MAT03.NoParameter"));
            return;
        }

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        _logger.AdminAction(
            "MAT03",
            "OpenMaterialUsage",
            _currentUser.DisplayName,
            $"MaterialNumber={materialNumber}; Root={storagePaths.RootDirectory}");

        var view = new SapMaterialUsageView(
            materialNumber,
            storagePaths,
            GetSapMaterialStatusRuleService(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args));

        view.TransactionRequested += ExecuteTransactionFromView;

        WorkspacePanel.Children.Add(view);
        ResetWorkspaceScroll();
    }
}