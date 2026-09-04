using DMS.Desktop.Views.Framework;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkHub(string frameworkCode)
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new FrameworkHubView(
            frameworkCode,
            executeTransaction: ExecuteTransaction,
            translate: key => T(key)));
        ResetWorkspaceScroll();

        _logger.AdminAction(
            frameworkCode,
            "FRAMEWORK_OPEN",
            _currentUser.DisplayName,
            $"FrameworkCode={frameworkCode}");
    }

    private void RenderFrameworkRuntimeConfiguration()
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new FrameworkRuntimeConfigurationView(
            environment: _appSettings.Environment,
            configurationMode: _appSettings.ConfigurationMode,
            configRoot: _appSettings.ConfigurationRootPath,
            dataRoot: GetDmsDataRootPath(),
            documentsRoot: _appSettings.DocumentsRootPath,
            logsRoot: _appSettings.LogsRootPath,
            brandingRoot: _appSettings.BrandingRootPath,
            articlesDataPath: _appSettings.ArticlesDataPath,
            sapMode: _appSettings.SapMode,
            mesMode: _appSettings.MesMode,
            databaseMode: _appSettings.DatabaseMode,
            translate: key => T(key),
            log: (action, details) => _logger.AdminAction(
                "FW03",
                action,
                _currentUser.DisplayName,
                details)));
        ResetWorkspaceScroll();
    }

    private void RenderFrameworkDiagnostics()
    {
        WorkspacePanel.Children.Clear();
        WorkspacePanel.Children.Add(new FrameworkDiagnosticsView(
            configRoot: _appSettings.ConfigurationRootPath,
            dataRoot: GetDmsDataRootPath(),
            translate: key => T(key),
            log: (action, details) => _logger.AdminAction(
                "FW04",
                action,
                _currentUser.DisplayName,
                details)));
        ResetWorkspaceScroll();
    }
}
