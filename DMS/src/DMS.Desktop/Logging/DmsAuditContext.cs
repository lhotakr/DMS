namespace DMS.Desktop.Logging;

public sealed record DmsAuditContext
{
    public string CorrelationId { get; init; } = DmsLogger.NewOperationId();
    public string TransactionCode { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string Entity { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string PersonId { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
}
