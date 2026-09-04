namespace DMS.Desktop.WorkLog;

public sealed class WorkLogSettings
{
    public const string DefaultDatabasePath = @"Z:\SAP\DMS-db\DEV\WorkLog.db";

    public string DatabasePath { get; set; } = DefaultDatabasePath;

    // Reserved for the future unattended WorkLog server client.
    // Phase 1 only stores these values; it does not start background jobs.
    public bool ServerClientEnabled { get; set; }

    public int ServerTaskIntervalMinutes { get; set; } = 15;
}
