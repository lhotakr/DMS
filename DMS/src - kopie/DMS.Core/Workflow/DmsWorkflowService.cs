namespace DMS.Core.Workflow;

public sealed class DmsWorkflowService
{
    public IReadOnlyList<DmsWorkflowTransitionDefinition> GetAvailableTransitions(
        DmsWorkflowDefinition workflow,
        string currentState) =>
        workflow.Transitions
            .Where(x => string.Equals(x.FromState, currentState, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public DmsWorkflowEvaluationResult EvaluateTransition(
        DmsWorkflowTransitionDefinition transition,
        IEnumerable<string> userRoles,
        string? currentUser = null,
        string? entityAuthor = null)
    {
        if (transition.RequiresDifferentUser &&
            !string.IsNullOrWhiteSpace(currentUser) &&
            !string.IsNullOrWhiteSpace(entityAuthor) &&
            string.Equals(currentUser, entityAuthor, StringComparison.OrdinalIgnoreCase))
        {
            return new DmsWorkflowEvaluationResult
            {
                Allowed = false,
                ReasonCode = "SAME_USER_NOT_ALLOWED"
            };
        }

        var required = transition.AllowedRoleCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (required.Count == 0)
        {
            return new DmsWorkflowEvaluationResult
            {
                Allowed = true,
                ReasonCode = "NO_ROLE_REQUIRED"
            };
        }

        var roles = (userRoles ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matching = required.Where(roles.Contains).ToList();

        return matching.Count > 0
            ? new DmsWorkflowEvaluationResult
            {
                Allowed = true,
                ReasonCode = "ROLE_MATCH",
                MatchingRoles = matching
            }
            : new DmsWorkflowEvaluationResult
            {
                Allowed = false,
                ReasonCode = "ROLE_REQUIRED"
            };
    }

    public bool IsKnownState(DmsWorkflowDefinition workflow, string state) =>
        workflow.States.Any(x =>
            string.Equals(x.Code, state, StringComparison.OrdinalIgnoreCase));

    public bool IsKnownTransition(
        DmsWorkflowDefinition workflow,
        string fromState,
        string toState) =>
        workflow.Transitions.Any(x =>
            string.Equals(x.FromState, fromState, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ToState, toState, StringComparison.OrdinalIgnoreCase));
}
