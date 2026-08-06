using DMS.Desktop.Views.Mes;
using DMS.Integration.Mes.Services;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private string GetMesSettingsFilePath()
    {
        return GetConfigPath("mes-communication-settings.json");
    }

    private string GetMesDevicesFilePath()
    {
        var fallback = GetConfigPath("devices.txt");
        try
        {
            var settings = new MesCommunicationSettingsService().Load(GetMesSettingsFilePath());
            return ResolveMesConfiguredPath(settings.DevicesFilePath, fallback, "devices.txt");
        }
        catch
        {
            return fallback;
        }
    }

    private string GetMesStationsFilePath()
    {
        var fallback = GetConfigPath("mes-stations.json");
        try
        {
            var settings = new MesCommunicationSettingsService().Load(GetMesSettingsFilePath());
            return ResolveMesConfiguredPath(settings.StationsFilePath, fallback, "mes-stations.json");
        }
        catch
        {
            return fallback;
        }
    }

    private string GetMesStationSnapshotFolder()
    {
        var fallback = Path.Combine(GetDmsDataRootPath(), "Data", "MES", "StationSnapshots");
        try
        {
            var settings = new MesCommunicationSettingsService().Load(GetMesSettingsFilePath());
            return ResolveMesConfiguredPath(settings.StationSnapshotsFolder, fallback, string.Empty);
        }
        catch
        {
            return fallback;
        }
    }

    private string ResolveMesConfiguredPath(string? configuredPath, string fallback, string defaultFileName)
    {
        var value = configuredPath?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Path.IsPathRooted(value))
        {
            return Directory.Exists(value) && !string.IsNullOrWhiteSpace(defaultFileName)
                ? Path.Combine(value, defaultFileName)
                : value;
        }

        var resolvedRelativePath = GetConfigPath(value);
        return Directory.Exists(resolvedRelativePath) && !string.IsNullOrWhiteSpace(defaultFileName)
            ? Path.Combine(resolvedRelativePath, defaultFileName)
            : resolvedRelativePath;
    }

    private string GetMesWorkplaceSnapshotPath()
    {
        return Path.Combine(
            GetDmsDataRootPath(),
            "Data",
            "MES",
            "mes05-last-snapshot.json");
    }

    private void RenderMesCommunicationSettings()
    {
        WorkspacePanel.Children.Clear();

        var settingsPath = GetMesSettingsFilePath();

        _logger.AdminAction(
            "MES00",
            "OpenMesCommunicationSettings",
            _currentUser.DisplayName,
            $"SettingsFile={settingsPath}");

        WorkspacePanel.Children.Add(new MesCommunicationSettingsView(
            settingsPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderMesDeviceEditor()
    {
        WorkspacePanel.Children.Clear();

        var devicesFilePath = GetMesDevicesFilePath();

        _logger.AdminAction(
            "MES02",
            "OpenMesDeviceEditor",
            _currentUser.DisplayName,
            $"DevicesFile={devicesFilePath}");

        WorkspacePanel.Children.Add(new MesDeviceEditorView(
            devicesFilePath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderMesStationData()
    {
        WorkspacePanel.Children.Clear();

        var stationsFilePath = GetMesStationsFilePath();
        var settingsPath = GetMesSettingsFilePath();
        var snapshotFolder = GetMesStationSnapshotFolder();

        _logger.AdminAction(
            "MES03",
            "OpenMesStationData",
            _currentUser.DisplayName,
            $"StationsFile={stationsFilePath}; SettingsFile={settingsPath}; SnapshotFolder={snapshotFolder}");

        WorkspacePanel.Children.Add(new MesStationDataView(
            stationsFilePath,
            settingsPath,
            snapshotFolder,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }

    private void RenderMesWorkplaceOverview()
    {
        WorkspacePanel.Children.Clear();

        var devicesFilePath = GetMesDevicesFilePath();
        var settingsPath = GetMesSettingsFilePath();
        var snapshotPath = GetMesWorkplaceSnapshotPath();

        _logger.AdminAction(
            "MES05",
            "OpenMesWorkplaceOverview",
            _currentUser.DisplayName,
            $"DevicesFile={devicesFilePath}; SettingsFile={settingsPath}; Snapshot={snapshotPath}");

        WorkspacePanel.Children.Add(new MesWorkplaceOverviewView(
            devicesFilePath,
            settingsPath,
            snapshotPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key),
            translateFormat: (key, args) => T(key, args)));

        ResetWorkspaceScroll();
    }
}
