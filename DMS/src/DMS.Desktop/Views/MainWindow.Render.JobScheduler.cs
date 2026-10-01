using DMS.Desktop.Views.Scheduling;
using System.IO;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderJobScheduler()

    {
        WorkspacePanel.Children.Clear();

        var schedulerRoot =
            Path.Combine(
                GetDmsDataRootPath(),
                "Scheduler");

        Directory.CreateDirectory(
            schedulerRoot);

        var jobsPath =
            Path.Combine(
                schedulerRoot,
                "jobs.json");

        var templatesPath =
            Path.Combine(
                schedulerRoot,
                "report-templates.json");

        var mesSettingsPath =
            GetMesDatabaseSettingsFilePath();

        WorkspacePanel.Children.Add(
        new Job10SchedulerView(
            jobsPath,
            templatesPath,
            mesSettingsPath,
            _logger,
            _currentUser.DisplayName,
            translate: key => T(key)));

        ResetWorkspaceScroll();
    }
}
