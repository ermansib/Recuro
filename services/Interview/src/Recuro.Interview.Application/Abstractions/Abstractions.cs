using Recuro.Interview.Domain.Applications;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Rules;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Application.Abstractions;

/// <summary>Rounds of the current tenant. Never returns another tenant's rows.</summary>
public interface IInterviewRepository
{
    Task<InterviewRound?> GetAsync(Guid id, CancellationToken ct);

    Task<InterviewRound?> GetByAssessmentAsync(Guid assessmentId, CancellationToken ct);

    /// <summary>Every round of an application, oldest first.</summary>
    Task<IReadOnlyList<InterviewRound>> ListForApplicationAsync(string appId, CancellationToken ct);

    /// <summary>Scheduled rounds whose reminder or overdue time has passed and that still wait for feedback.</summary>
    Task<IReadOnlyList<InterviewRound>> ListSlaDueAsync(DateTimeOffset now, int limit, CancellationToken ct);

    void Add(InterviewRound round);
}

public interface IApplicationTrackRepository
{
    Task<ApplicationTrack?> GetAsync(string appId, CancellationToken ct);

    void Add(ApplicationTrack track);
}

public interface ISelectionRepository
{
    Task<SelectionDecision?> GetByAppIdAsync(string appId, CancellationToken ct);

    Task<SelectionDecision?> GetByWorkflowAsync(Guid workflowInstanceId, CancellationToken ct);

    void Add(SelectionDecision decision);
}

/// <summary>
/// The interview rules (round templates, feedback SLA, ratification) from the Config service, or the
/// FRD defaults while Config has no <c>interview</c> matrix for the tenant.
/// </summary>
public interface IInterviewRulesSource
{
    Task<InterviewRules> GetAsync(CancellationToken ct);
}

/// <summary>The requisition's job description from the Requisition service (S-05), read on behalf of the caller.</summary>
public interface IJobDescriptions
{
    /// <summary>Null when the requisition has no JD.</summary>
    Task<JobDescriptionView?> GetAsync(string reqId, CancellationToken ct);
}

/// <summary>The JD fields scheduling uses: the grade, the competencies to rate and the assessment material.</summary>
public sealed record JobDescriptionView(string ReqId, string Grade, IReadOnlyList<string> Competencies, IReadOnlyList<string> Assessments);

/// <summary>The Identity service's PDP (<c>POST /api/v1/identity/decide</c>, RCU-AUT-003).</summary>
public interface IAccessDecisions
{
    Task<bool> AllowedAsync(string action, string resourceType, string resourceId, IReadOnlyList<string> assigneeIds, CancellationToken ct);
}

/// <summary>Calendar adapter (RCU-INT-001/007): invites for the panel. The default logs; a tenant plugs in its calendar.</summary>
public interface ICalendarInvites
{
    Task SendAsync(CalendarInvite invite, CancellationToken ct);

    Task CancelAsync(CalendarInvite invite, CancellationToken ct);
}

/// <summary>One invite: the round id makes it idempotent at the calendar.</summary>
public sealed record CalendarInvite(Guid InterviewId, string Title, DateTimeOffset Start, DateTimeOffset End, InterviewMode Mode, IReadOnlyList<string> AttendeeIds);

/// <summary>The Workflow service's instance API (POST /api/v1/workflows, RCU-WFL-001).</summary>
public interface IWorkflowClient
{
    /// <summary>Opens a workflow. Idempotent on <see cref="WorkflowStart.CorrelationKey"/>.</summary>
    Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct);

    /// <summary>Saga compensation: withdraws an instance opened for a step that then failed.</summary>
    Task CancelAsync(Guid instanceId, string reason, CancellationToken ct);
}

public sealed record WorkflowStart(
    string Type,
    WorkflowSubject Subject,
    string ConfigVersionId,
    string CorrelationKey,
    IReadOnlyList<WorkflowLeg> Legs,
    WorkflowPresentation Presentation);

public sealed record WorkflowSubject(string Type, string Id);

public sealed record WorkflowLeg(string Name, IReadOnlyList<WorkflowAssignee> Assignees, int? SlaWorkingDays, IReadOnlyList<WorkflowEscalation> Escalation);

public sealed record WorkflowAssignee(string Role, string Label);

public sealed record WorkflowEscalation(string Role, string Label, int AfterWorkingDays);

/// <summary>How the approvals inbox shows the task (frontend <c>ApprovalItem</c> display fields).</summary>
public sealed record WorkflowPresentation(
    string Kind,
    string Tone,
    string Title,
    string Meta,
    string Route,
    WorkflowChip? Chip,
    IReadOnlyList<WorkflowAction> Actions);

public sealed record WorkflowChip(string Text, string Tone);

public sealed record WorkflowAction(string Id, string Label, string Style, string Effect, string? ResultText);

/// <summary>A service this one depends on did not answer. The API maps it to 503 so the client can retry.</summary>
public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException()
    {
    }

    public DependencyUnavailableException(string message)
        : base(message)
    {
    }

    public DependencyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
