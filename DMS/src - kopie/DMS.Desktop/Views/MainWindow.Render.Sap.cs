using DMS.Core.Sap;
using DMS.Desktop.Views.Sap;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderSapCockpit()
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());
        storagePaths.EnsureDirectories();

        var materialRulesPath = storagePaths.MaterialRangesFilePath;

        _logger.AdminAction(
            "SAP00",
            "OpenSapCockpit",
            _currentUser.DisplayName,
            $"Root={storagePaths.RootDirectory}; MaterialRules={materialRulesPath}; SapMaterials={storagePaths.SapMaterialsFilePath}");

        WorkspacePanel.Children.Add(new SapCockpitView(
            storagePaths,
            materialRulesPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderSapMaterialDisplay(string materialNumber)
    {
        WorkspacePanel.Children.Clear();

        var storagePaths = new SapStoragePaths(GetDmsDataRootPath());

        _logger.AdminAction(
            "SAP03",
            "OpenMaterialDisplay",
            _currentUser.DisplayName,
            $"MaterialNumber={materialNumber}; File={storagePaths.SapMaterialsFilePath}");

        WorkspacePanel.Children.Add(new SapMaterialDisplayView(
            materialNumber,
            storagePaths,
            _decorationRuleService,
            GetSapMaterialStatusRuleService(),
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private SapMaterialStatusRuleService GetSapMaterialStatusRuleService()
    {
        var path = GetConfigPath("sap-material-status-rules.json");
        var rules = new SapMaterialStatusRuleLoader().LoadFromJson(path);
        return new SapMaterialStatusRuleService(rules);
    }
}