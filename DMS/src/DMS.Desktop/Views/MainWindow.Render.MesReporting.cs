using DMS.Desktop.Views.Mes;
using DMS.Core.Sap;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderMesDatabaseSettings()
    {
        WorkspacePanel.Children.Clear();

        var settingsPath =
            GetMesDatabaseSettingsFilePath();

        _logger.AdminAction(
            "MESSET",
            "OpenMesDatabaseSettings",
            _currentUser.DisplayName,
            $"SettingsFile={settingsPath}");

        WorkspacePanel.Children.Add(
            new MesDatabaseSettingsView(
                settingsPath,
                _logger,
                _currentUser.DisplayName,
                _currentUser.HasRole("DMS_ADMIN"),
                translate: key => T(key),
                connectionChanged: async () =>
                {
                    ConfigureMesDatabaseHealthTimer();
                    await RefreshMesDatabaseStatusAsync();
                }));

        ResetWorkspaceScroll();
    }

    private void RenderMesReporting()
    {
        WorkspacePanel.Children.Clear();

        var settingsPath =
            GetMesDatabaseSettingsFilePath();

        var definitionsPath =
            GetMesReportDefinitionsFilePath();

        var automationTemplatesPath =
            Path.Combine(
                GetDmsDataRootPath(),
                "Scheduler",
                "report-templates.json");

        var sapStoragePaths =
            new SapStoragePaths(
                GetDmsDataRootPath());

        _logger.AdminAction(
            "MES06",
            "OpenMesReporting",
            _currentUser.DisplayName,
            $"SettingsFile={settingsPath}; DefinitionsFile={definitionsPath}");

        WorkspacePanel.Children.Add(

        new MesReportingView(
            settingsPath,
            definitionsPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            automationTemplatesPath: automationTemplatesPath,
            sapMaterialsFilePath:
                sapStoragePaths.SapMaterialsFilePath)
        );

        ResetWorkspaceScroll();
    }
}
