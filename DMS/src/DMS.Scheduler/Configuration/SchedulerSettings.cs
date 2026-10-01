using System;
using System.IO;

namespace DMS.Scheduler.Configuration;

public sealed class SchedulerSettings
{
    public string EnvironmentName { get; set; } = "DEV";
    public string DataRoot { get; set; } = @"Z:\SAP\DMS-db\DEV";
    public string ServiceName { get; set; } = "DMS.Scheduler";
    public int PollingSeconds { get; set; } = 20;

    public string SchedulerRoot =>
        Path.Combine(DataRoot, "Scheduler");

    public string JobsPath =>
        Path.Combine(SchedulerRoot, "jobs.json");

    public string TemplatesPath =>
        Path.Combine(SchedulerRoot, "report-templates.json");

    public string MesDatabaseSettingsPath =>
        ResolveMesSettingsPath();

    public string LogPath =>
        Path.Combine(SchedulerRoot, "scheduler.log");

    public string OutputPath =>
        Path.Combine(SchedulerRoot, "Output");
    public SmtpSettings Smtp { get; set; } = new();
    public SharePointSettings SharePoint { get; set; } = new();

    private string ResolveMesSettingsPath()
    {
        var json =
            Path.Combine(
                DataRoot,
                "Config",
                "mes-database-settings.json");

        if (File.Exists(json))
            return json;

        // Windows may hide extensions, and older deployments may use the file
        // without an explicit .json suffix.
        return Path.Combine(
            DataRoot,
            "Config",
            "mes-database-settings");
    }

    public void Normalize()
    {
        EnvironmentName =
            string.IsNullOrWhiteSpace(EnvironmentName)
                ? "DEV"
                : EnvironmentName.Trim();

        DataRoot =
            Environment.ExpandEnvironmentVariables(
                DataRoot?.Trim() ?? string.Empty);

        ServiceName =
            string.IsNullOrWhiteSpace(ServiceName)
                ? "DMS.Scheduler"
                : ServiceName.Trim();

        PollingSeconds =
            Math.Clamp(
                PollingSeconds,
                5,
                3600);
    }
}
