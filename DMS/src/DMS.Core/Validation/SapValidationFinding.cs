namespace DMS.Core.Sap.Validation;

public sealed class SapValidationFinding
{
    public string RuleId { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    /// <summary>
    /// Info, Warning, Error
    /// </summary>
    public string Severity { get; init; } = "Warning";

    public string Message { get; init; } = string.Empty;
}