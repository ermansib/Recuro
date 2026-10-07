namespace Recuro.Workflow.Domain.Workflows;

public enum WorkflowStatus
{
    Active,
    Approved,
    Rejected,
    Cancelled,
}

public enum ApprovalTaskStatus
{
    Open,
    Completed,
    Cancelled,
}

/// <summary>What an inbox action does (frontend <c>ApprovalAction.effect</c>).</summary>
public enum ActionEffect
{
    /// <summary>Completes the task in favour (approve, sign off, acknowledge).</summary>
    Resolve,

    /// <summary>Completes the task against; a documented reason is mandatory and the instance ends Rejected.</summary>
    Reject,

    /// <summary>Raises a query to the initiator and pauses the SLA clock (RCU-WFL-004).</summary>
    Query,
}

/// <summary>
/// One leg of a route, as resolved by the Config service (architecture.md "Synchronous contracts").
/// Legs run in order; the assignees of one leg work in parallel and all must approve (AND-parallel).
/// </summary>
public sealed record LegDefinition(string Name, IReadOnlyList<LegAssignee> Assignees, int? SlaWorkingDays, IReadOnlyList<LegEscalation> Escalation);

public sealed record LegAssignee(string Role, string Label);

/// <summary>Who hears about a breach, and how many working days after the deadline (RCU-WFL-006).</summary>
public sealed record LegEscalation(string Role, string Label, int AfterWorkingDays);

/// <summary>How the approvals inbox shows the instance's tasks (frontend <c>ApprovalItem</c> display fields).</summary>
public sealed record Presentation(
    string Kind,
    string Tone,
    string Title,
    string Meta,
    string Route,
    SensitiveValue? Sensitive,
    IReadOnlyList<InboxAction> Actions,
    InboxChip? Chip);

public sealed record SensitiveValue(string Label, string Value);

public sealed record InboxAction(string Id, string Label, string Style, ActionEffect Effect, string? ResultText);

/// <summary>A fixed chip for tasks without an SLA (e.g. "Annexure D"). Tasks with an SLA get a computed chip.</summary>
public sealed record InboxChip(string Text, string Tone);

/// <summary>Deadlines for one task, computed on the tenant's business calendar before the task is created.</summary>
/// <param name="DueAt">SLA deadline; null when the leg has no SLA.</param>
/// <param name="EscalationsAt">When each escalation step of the leg fires, in order.</param>
public sealed record TaskSchedule(DateTimeOffset? DueAt, IReadOnlyList<DateTimeOffset> EscalationsAt)
{
    public static TaskSchedule None { get; } = new(null, []);
}

/// <summary>An escalation step pinned on a task, with the time it fires and, once fired, when.</summary>
public sealed record EscalationStep(int Level, string Role, string Label, DateTimeOffset At, DateTimeOffset? FiredAt);

/// <summary>The decision recorded on a task (frontend <c>ApprovalItem.decision</c>). Immutable once set.</summary>
public sealed record TaskDecision(string ActionId, ActionEffect Effect, string Text, string ById, string ByName, DateTimeOffset At, string? Reason);

/// <summary>Who acts, from the validated token.</summary>
public sealed record Actor(string Id, string Name, IReadOnlyCollection<string> Roles);
