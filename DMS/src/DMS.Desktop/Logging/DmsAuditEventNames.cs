namespace DMS.Desktop.Logging;

public static class DmsAuditEventNames
{
    public const string TransactionStart = "TX_START";
    public const string TransactionSucceeded = "TX_OK";
    public const string TransactionFailed = "TX_FAIL";
    public const string TransactionDenied = "TX_DENIED";
    public const string TransactionValidationFailed = "TX_VALIDATION";

    public const string AuditCreate = "AUDIT_CREATE";
    public const string AuditUpdate = "AUDIT";
    public const string AuditDelete = "AUDIT_DELETE";

    public const string ConfigurationChanged = "CONFIG_CHANGED";
    public const string WorkflowChanged = "WORKFLOW_CHANGED";
    public const string SecurityChanged = "SECURITY_CHANGED";
    public const string FrameworkDiagnostic = "FRAMEWORK_DIAGNOSTIC";
    public const string FrameworkHealthCheck = "FRAMEWORK_HEALTH";
}
