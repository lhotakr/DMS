namespace DMS.Integration.Mes.Database;

public sealed class MesDatabaseConnectionSettings
{
    public bool IsEnabled { get; set; } = true;

    public string Server { get; set; } = "CZE-SQL01";

    public string Database { get; set; } = "FastecCZE";

    public string ReportingSchema { get; set; } = "ana";

    public bool IntegratedSecurity { get; set; } = true;

    public bool Encrypt { get; set; } = true;

    public bool TrustServerCertificate { get; set; } = true;

    public int ConnectTimeoutSeconds { get; set; } = 5;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int HealthCheckIntervalSeconds { get; set; } = 60;

    public int DefaultReportLookbackHours { get; set; } = 24;

    public int DefaultReportMaxRows { get; set; } = 5000;

    public string ApplicationName { get; set; } = "DMS MES Reporting";

    public void Normalize()
    {
        Server = string.IsNullOrWhiteSpace(Server)
            ? "CZE-SQL01"
            : Server.Trim();

        Database = string.IsNullOrWhiteSpace(Database)
            ? "FastecCZE"
            : Database.Trim();

        ReportingSchema = string.IsNullOrWhiteSpace(ReportingSchema)
            ? "ana"
            : ReportingSchema.Trim();

        ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, 1, 60);
        CommandTimeoutSeconds = Math.Clamp(CommandTimeoutSeconds, 5, 600);
        HealthCheckIntervalSeconds = Math.Clamp(HealthCheckIntervalSeconds, 15, 3600);
        DefaultReportLookbackHours = Math.Clamp(DefaultReportLookbackHours, 1, 24 * 90);
        DefaultReportMaxRows = Math.Clamp(DefaultReportMaxRows, 100, 50000);

        ApplicationName = string.IsNullOrWhiteSpace(ApplicationName)
            ? "DMS MES Reporting"
            : ApplicationName.Trim();

        // v1 intentionally supports Windows authentication only.
        IntegratedSecurity = true;
    }
}
