namespace DMS.Core.Workflow;

public static class DmsWorkflowCatalog
{
    public const string ChecklistDefaultCode = "CHECKLIST_DEFAULT";

    public static DmsWorkflowDefinition CreateChecklistDefault() => new()
    {
        Code = ChecklistDefaultCode,
        Name = "Checklist lifecycle",
        EntityType = "Checklist",
        InitialState = "Draft",
        States = new[]
        {
            State("Draft", editable: true),
            State("InProgress", editable: true),
            State("SubmittedForReview"),
            State("ReturnedForCorrection", editable: true),
            State("Checked"),
            State("Closed", terminal: true),
            State("Cancelled", terminal: true)
        },
        Transitions = new[]
        {
            Transition("START", "Draft", "InProgress"),
            Transition("SUBMIT", "Draft", "SubmittedForReview"),
            Transition("SUBMIT", "InProgress", "SubmittedForReview"),
            Transition("RESUBMIT", "ReturnedForCorrection", "SubmittedForReview"),
            Transition("RETURN", "SubmittedForReview", "ReturnedForCorrection", differentUser: true),
            Transition("APPROVE", "SubmittedForReview", "Checked", differentUser: true),
            Transition("REOPEN", "ReturnedForCorrection", "InProgress"),
            Transition("CLOSE", "Checked", "Closed"),
            Transition("CANCEL", "Draft", "Cancelled"),
            Transition("CANCEL", "InProgress", "Cancelled")
        }
    };

    private static DmsWorkflowStateDefinition State(
        string code,
        bool editable = false,
        bool terminal = false) => new()
    {
        Code = code,
        IsEditable = editable,
        IsTerminal = terminal
    };

    private static DmsWorkflowTransitionDefinition Transition(
        string code,
        string from,
        string to,
        bool differentUser = false) => new()
    {
        Code = code,
        FromState = from,
        ToState = to,
        RequiresDifferentUser = differentUser
    };
}
