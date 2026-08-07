using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DMS.Core.Checklists;
using DMS.Core.Workflow;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkWorkflowView : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataRoot;
    private readonly IReadOnlyList<string> _userRoles;
    private readonly string _currentUser;
    private readonly Func<string, string> _translate;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string> _log;
    private readonly DmsWorkflowService _workflowService = new();
    private readonly List<DmsWorkflowDefinition> _workflows = new()
    {
        DmsWorkflowCatalog.CreateChecklistDefault()
    };

    private List<ChecklistDefinition> _definitions = new();
    private List<ChecklistInstance> _instances = new();

    public FrameworkWorkflowView(
        string dataRoot,
        IReadOnlyList<string> userRoles,
        string currentUser,
        Func<string, string> translate,
        Action<string> executeTransaction,
        Action<string, string> log)
    {
        InitializeComponent();

        _dataRoot = dataRoot;
        _userRoles = userRoles;
        _currentUser = currentUser;
        _translate = translate;
        _executeTransaction = executeTransaction;
        _log = log;

        ApplyLocalization();
        Loaded += (_, _) => Reload();
    }

    private string T(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TitleText.Text = T("Framework.FW07.Title", "FW07 — Workflow framework");
        SubtitleText.Text = T("Framework.FW07.Description", "Shows lifecycle states, valid transitions and current checklist workflow policies without modifying business data.");
        WorkflowLabel.Text = T("Framework.FW07.Workflow", "Workflow");
        StateLabel.Text = T("Framework.FW07.State", "Evaluate state");
        ChecklistButton.Content = T("Framework.FW07.OpenChecklists", "Open CHL05");

        StateCodeColumn.Header = T("Framework.FW07.Column.State", "State");
        EditableColumn.Header = T("Framework.FW07.Column.Editable", "Editable");
        TerminalColumn.Header = T("Framework.FW07.Column.Terminal", "Terminal");
        InstanceCountColumn.Header = T("Framework.FW07.Column.Instances", "Instances");

        ActionColumn.Header = T("Framework.FW07.Column.Action", "Action");
        FromColumn.Header = T("Framework.FW07.Column.From", "From");
        ToColumn.Header = T("Framework.FW07.Column.To", "To");
        RoleColumn.Header = T("Framework.FW07.Column.Roles", "Required roles");
        AllowedColumn.Header = T("Framework.FW07.Column.Allowed", "Allowed");
        ReasonColumn.Header = T("Framework.FW07.Column.Reason", "Reason");

        DefinitionColumn.Header = T("Framework.FW07.Column.Definition", "Definition");
        DefinitionNameColumn.Header = T("Framework.FW07.Column.Name", "Name");
        ReviewColumn.Header = T("Framework.FW07.Column.Review", "Review");
        ApproverColumn.Header = T("Framework.FW07.Column.Approvers", "Approvers");
        InstancesColumn.Header = T("Framework.FW07.Column.Instances", "Instances");
        WaitingColumn.Header = T("Framework.FW07.Column.Waiting", "Waiting for review");

        FooterText.Text = T("Framework.FW07.Footer", "FW07 is read-only. Checklist structure and approval policy remain administered in CHL00. This transaction validates and explains lifecycle behavior.");
    }

    private void Reload()
    {
        LoadChecklistData();

        WorkflowCombo.ItemsSource = _workflows;
        WorkflowCombo.SelectedIndex = 0;

        RefreshWorkflow();

        _log(
            "WORKFLOW_OVERVIEW",
            $"Definitions={_definitions.Count}; Instances={_instances.Count}");
    }

    private void LoadChecklistData()
    {
        var checklistRoot = Path.Combine(_dataRoot, "Data", "Checklists");
        var definitionsRoot = Path.Combine(checklistRoot, "Definitions");
        var instancesRoot = Path.Combine(checklistRoot, "Instances");

        _definitions = Directory.Exists(definitionsRoot)
            ? Directory.EnumerateFiles(definitionsRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Select(TryRead<ChecklistDefinition>)
                .Where(x => x is not null)
                .Cast<ChecklistDefinition>()
                .OrderBy(x => x.Code)
                .ToList()
            : new List<ChecklistDefinition>();

        _instances = Directory.Exists(instancesRoot)
            ? Directory.EnumerateFiles(instancesRoot, "*.json", SearchOption.AllDirectories)
                .Select(TryRead<ChecklistInstance>)
                .Where(x => x is not null)
                .Cast<ChecklistInstance>()
                .ToList()
            : new List<ChecklistInstance>();
    }

    private static TItem? TryRead<TItem>(string path) where TItem : class
    {
        try
        {
            return JsonSerializer.Deserialize<TItem>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void WorkflowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshWorkflow();

    private void StateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshTransitions();

    private void ChecklistButton_Click(object sender, RoutedEventArgs e) =>
        _executeTransaction("CHL05");

    private void RefreshWorkflow()
    {
        if (WorkflowCombo.SelectedItem is not DmsWorkflowDefinition workflow)
        {
            return;
        }

        StatesGrid.ItemsSource = workflow.States
            .Select(state => new StateRow(
                state.Code,
                state.IsEditable,
                state.IsTerminal,
                _instances.Count(x =>
                    string.Equals(
                        x.Status.ToString(),
                        state.Code,
                        StringComparison.OrdinalIgnoreCase))))
            .ToList();

        StateCombo.ItemsSource = workflow.States;
        StateCombo.SelectedItem = workflow.States.FirstOrDefault(x =>
            string.Equals(
                x.Code,
                workflow.InitialState,
                StringComparison.OrdinalIgnoreCase));

        DefinitionsGrid.ItemsSource = _definitions
            .Select(definition => new DefinitionRow(
                definition.Code,
                definition.Name,
                definition.RequiresReview
                    ? T("Framework.FW07.Yes", "Yes")
                    : T("Framework.FW07.No", "No"),
                BuildApprovers(definition),
                _instances.Count(x =>
                    string.Equals(
                        x.DefinitionCode,
                        definition.Code,
                        StringComparison.OrdinalIgnoreCase)),
                _instances.Count(x =>
                    string.Equals(
                        x.DefinitionCode,
                        definition.Code,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Status == ChecklistStatus.SubmittedForReview)))
            .ToList();

        SummaryText.Text = string.Format(
            T(
                "Framework.FW07.Summary",
                "{0} states | {1} transitions | {2} checklist definitions"),
            workflow.States.Count,
            workflow.Transitions.Count,
            _definitions.Count);

        RefreshTransitions();
    }

    private string BuildApprovers(ChecklistDefinition definition)
    {
        var values = new List<string>();
        values.AddRange(definition.AllowedApprovalRoleCodes);
        values.AddRange(definition.AllowedApprovalPersonIds.Select(x => $"Person:{x}"));
        values.AddRange(definition.AllowedApprovalOrganizationUnitIds.Select(x => $"Org:{x}"));

        return values.Count == 0
            ? T("Framework.FW07.AnyChl06", "Any user allowed by CHL06")
            : string.Join(", ", values);
    }

    private void RefreshTransitions()
    {
        if (WorkflowCombo.SelectedItem is not DmsWorkflowDefinition workflow ||
            StateCombo.SelectedItem is not DmsWorkflowStateDefinition state)
        {
            TransitionsGrid.ItemsSource = null;
            return;
        }

        TransitionsGrid.ItemsSource = _workflowService
            .GetAvailableTransitions(workflow, state.Code)
            .Select(transition =>
            {
                var result = _workflowService.EvaluateTransition(
                    transition,
                    _userRoles,
                    _currentUser,
                    entityAuthor: null);

                return new TransitionRow(
                    transition.Code,
                    transition.FromState,
                    transition.ToState,
                    transition.AllowedRoleCodes.Count == 0
                        ? T("Framework.FW07.PolicySpecific", "Definition / transaction policy")
                        : string.Join(", ", transition.AllowedRoleCodes),
                    result.Allowed,
                    Describe(result));
            })
            .ToList();
    }

    private string Describe(DmsWorkflowEvaluationResult result) =>
        result.ReasonCode switch
        {
            "NO_ROLE_REQUIRED" => T("Framework.FW07.Reason.Policy", "Allowed by workflow; business policy is evaluated separately"),
            "ROLE_MATCH" => string.Format(
                T("Framework.FW07.Reason.Match", "Matched: {0}"),
                string.Join(", ", result.MatchingRoles)),
            "ROLE_REQUIRED" => T("Framework.FW07.Reason.Role", "Required role is missing"),
            "SAME_USER_NOT_ALLOWED" => T("Framework.FW07.Reason.Author", "Author cannot execute this transition"),
            _ => result.ReasonCode
        };

    private sealed record StateRow(
        string Code,
        bool IsEditable,
        bool IsTerminal,
        int InstanceCount);

    private sealed record TransitionRow(
        string Action,
        string FromState,
        string ToState,
        string RequiredRoles,
        bool Allowed,
        string Reason);

    private sealed record DefinitionRow(
        string Code,
        string Name,
        string Review,
        string Approvers,
        int Instances,
        int Waiting);
}
