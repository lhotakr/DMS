namespace DMS.Core.Workflow;

public sealed class DmsWorkflowDefinition
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string InitialState { get; init; } = string.Empty;
    public IReadOnlyList<DmsWorkflowStateDefinition> States { get; init; } = Array.Empty<DmsWorkflowStateDefinition>();
    public IReadOnlyList<DmsWorkflowTransitionDefinition> Transitions { get; init; } = Array.Empty<DmsWorkflowTransitionDefinition>();
}

public sealed class DmsWorkflowStateDefinition
{
    public string Code { get; init; } = string.Empty;
    public bool IsEditable { get; init; }
    public bool IsTerminal { get; init; }
}

public sealed class DmsWorkflowTransitionDefinition
{
    public string Code { get; init; } = string.Empty;
    public string FromState { get; init; } = string.Empty;
    public string ToState { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedRoleCodes { get; init; } = Array.Empty<string>();
    public bool RequiresDifferentUser { get; init; }
}

public sealed record DmsWorkflowEvaluationResult
{
    public bool Allowed { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchingRoles { get; init; } = Array.Empty<string>();
}
