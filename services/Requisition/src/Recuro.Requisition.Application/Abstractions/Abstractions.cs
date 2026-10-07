using Recuro.Requisition.Domain.JobDescriptions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Abstractions;

/// <summary>Requisitions of the current tenant. Never returns another tenant's rows.</summary>
public interface IRequisitionRepository
{
    Task<ManpowerRequisition?> GetByReqIdAsync(string reqId, CancellationToken ct);

    Task<ManpowerRequisition?> GetByIdAsync(Guid id, CancellationToken ct);

    void Add(ManpowerRequisition requisition);

    Task<IReadOnlyList<ManpowerRequisition>> ListAsync(RequisitionFilter filter, CancellationToken ct);

    /// <summary>Requisitions in any of <paramref name="states"/>, and how many of them were raised since <paramref name="since"/>.</summary>
    Task<(int Total, int RaisedSince)> CountAsync(IReadOnlyCollection<RequisitionState> states, DateTimeOffset since, CancellationToken ct);
}

/// <summary>Tracker filters (RCU-REQ-007).</summary>
public sealed record RequisitionFilter(RequisitionState? State, string? Grade, string? Location, string? Text, int Limit);

public interface IJobDescriptionRepository
{
    Task<JobDescription?> GetByReqIdAsync(string reqId, CancellationToken ct);

    Task<JobDescription?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsForRequisitionAsync(Guid requisitionId, CancellationToken ct);

    void Add(JobDescription jobDescription);
}

/// <summary>Issues <c>REQ-YYYY-####</c> from a per-tenant, per-year sequence. Each call commits on its own, so numbers are never reused.</summary>
public interface IReqIdAllocator
{
    Task<string> NextAsync(int year, CancellationToken ct);
}

/// <summary>
/// The Config service's resolution API (architecture.md, "Synchronous contracts"). Rules are resolved,
/// never hard-coded here (RCU-CFG-002/003).
/// </summary>
public interface IRulesClient
{
    /// <summary>The DOA route for a grade at an instant. Null when Config doesn't know the grade.</summary>
    Task<DoaResolution?> ResolveDoaAsync(string grade, bool outOfBudget, DateTimeOffset at, CancellationToken ct);

    /// <summary>Adds working days on the tenant's business calendar.</summary>
    Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? location, string? versionId, CancellationToken ct);
}

public sealed record DoaResolution(
    string ConfigVersionId,
    string Grade,
    string BudgetStatus,
    string Initiating,
    string Recommending,
    string Approving,
    string ApproverRole,
    string BandLabel,
    OverallTat OverallTat,
    IReadOnlyList<RouteLeg> Legs);

public sealed record OverallTat(int MinDays, int MaxDays, string Label);

public sealed record RouteLeg(string Name, IReadOnlyList<RouteAssignee> Assignees, int? SlaWorkingDays, IReadOnlyList<RouteEscalation> Escalation);

public sealed record RouteAssignee(string Role, string Label);

public sealed record RouteEscalation(string Role, string Label, int AfterWorkingDays);

/// <summary>The Workflow service's instance API (POST /api/v1/workflows, RCU-WFL-001).</summary>
public interface IWorkflowClient
{
    /// <summary>Opens an approval workflow. Idempotent on <see cref="WorkflowStart.CorrelationKey"/>.</summary>
    Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct);

    /// <summary>Saga compensation: cancels an instance opened for a submit that then failed.</summary>
    Task CancelAsync(Guid instanceId, string reason, CancellationToken ct);
}

public sealed record WorkflowStart(
    string Type,
    WorkflowSubject Subject,
    string ConfigVersionId,
    string CorrelationKey,
    IReadOnlyList<RouteLeg> Legs,
    WorkflowPresentation Presentation);

public sealed record WorkflowSubject(string Type, string Id);

/// <summary>How the approvals inbox shows the task (frontend <c>ApprovalItem</c> display fields).</summary>
public sealed record WorkflowPresentation(
    string Kind,
    string Tone,
    string Title,
    string Meta,
    string Route,
    WorkflowSensitive? Sensitive,
    IReadOnlyList<WorkflowAction> Actions);

public sealed record WorkflowSensitive(string Label, string Value);

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
