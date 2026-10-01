namespace DMS.Scheduler.Configuration;

/// <summary>
/// Root delivery settings object persisted by SchedulerDeliverySettingsRepository.
/// Keep SMTP and SharePoint connection settings here so the repository has a single
/// strongly typed configuration root.
/// </summary>
public sealed class SchedulerDeliverySettings
{
    public SmtpSettings Smtp { get; set; } = new();
    public SharePointSettings SharePoint { get; set; } = new();
}

public class SmtpSettings
{
    public bool IsEnabled { get; set; } = true;
    public string Server { get; set; } = "10.131.10.5";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
    public bool UseAuthentication { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordEnvironmentVariable { get; set; }
        = "DMS_SCHEDULER_SMTP_PASSWORD";
    public string SenderAddress { get; set; }
        = "CZE-Fastec@heinz-glas.com";
    public string SenderDisplayName { get; set; }
        = "DMS Scheduler";
}

public class SharePointSettings
{
    public bool IsEnabled { get; set; } = true;
    public string AuthenticationMode { get; set; }
        = "InteractiveBrowser";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string SiteUrl { get; set; } = string.Empty;
    public string DocumentLibrary { get; set; }
        = "Shared Documents";
    public string ClientSecretEnvironmentVariable { get; set; }
        = "DMS_SCHEDULER_SP_CLIENT_SECRET";
}

// Compatibility aliases for older v9.x files that may still reference these names.
public sealed class SmtpDeliverySettings : SmtpSettings
{
}

public sealed class SharePointDeliverySettings : SharePointSettings
{
}
