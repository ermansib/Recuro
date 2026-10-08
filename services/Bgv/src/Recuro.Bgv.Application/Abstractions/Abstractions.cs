using Recuro.Bgv.Domain.Cases;
using Recuro.Bgv.Domain.Requests;

namespace Recuro.Bgv.Application.Abstractions;

/// <summary>BGV cases of the current tenant. Never returns another tenant's rows.</summary>
public interface IBgvCaseRepository
{
    /// <summary>Tracked: by case id or by application id (the frontend addresses cases both ways).</summary>
    Task<BgvCase?> FindAsync(string caseOrAppId, CancellationToken ct);

    Task<bool> ExistsForApplicationAsync(string appId, CancellationToken ct);

    /// <summary>Tracked: open cases still assigned to <paramref name="vendorId"/>.</summary>
    Task<IReadOnlyList<BgvCase>> ListOpenByVendorAsync(string vendorId, CancellationToken ct);

    /// <summary>Read-only: open cases waiting for another vendor (RCU-VND-003).</summary>
    Task<IReadOnlyList<BgvCase>> ListNeedingReassignmentAsync(CancellationToken ct);

    /// <summary>Open and under-review cases, and how many of them are due before <paramref name="dueBefore"/>.</summary>
    Task<(int InProgress, int DueSoon)> CountInProgressAsync(DateTimeOffset dueBefore, CancellationToken ct);

    void Add(BgvCase bgvCase);
}

public interface IBgvRequestRepository
{
    Task<BgvRequest?> GetAsync(string appId, CancellationToken ct);

    void Add(BgvRequest request);
}

/// <summary>The BGV matrix as Config resolved it, with the version to pin on the case.</summary>
public sealed record CheckMatrix(string ConfigVersionId, IReadOnlyList<CheckRule> Rules);

/// <summary>An escalation route from the Config escalation matrix.</summary>
public sealed record EscalationRoute(string ConfigVersionId, RoleRef First, RoleRef Final);

public sealed record RoleRef(string Role, string Label);

/// <summary>
/// The tenant's business rules from the Config service (resolve endpoints): the BGV check matrix, the BGV
/// TAT, the adverse-BGV escalation route and the business calendar. The only place Bgv reads rules.
/// </summary>
public interface IBgvRules
{
    /// <summary>The BGV matrix in force. Throws <see cref="DependencyUnavailableException"/> when Config can't be reached: a case pins its version.</summary>
    Task<CheckMatrix> GetCheckMatrixAsync(CancellationToken ct);

    /// <summary>The <c>bgv</c> stage TAT in working days (top of the §5.2 range).</summary>
    Task<int> GetTatWorkingDaysAsync(CancellationToken ct);

    /// <summary>The <c>adverse-bgv</c> escalation route.</summary>
    Task<EscalationRoute> GetAdverseEscalationAsync(CancellationToken ct);

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/> on the tenant's calendar.</summary>
    Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, CancellationToken ct);
}

/// <summary>A vendor as the Vendor service reports it (<c>GET /api/v1/vendors/{id}/status</c>).</summary>
public sealed record VendorStatus(string VendorId, string Name, string Type, bool Active)
{
    public const string BgvAgency = "BgvAgency";

    public bool IsActiveBgvAgency => Active && string.Equals(Type, BgvAgency, StringComparison.Ordinal);
}

/// <summary>The Vendor service. Unknown vendors are null.</summary>
public interface IVendorDirectory
{
    Task<VendorStatus?> GetAsync(string vendorId, CancellationToken ct);
}

/// <summary>The Workflow service's instance API (POST /api/v1/workflows, RCU-WFL-001).</summary>
public interface IWorkflowClient
{
    /// <summary>Opens an approval workflow. Idempotent on <see cref="WorkflowStart.CorrelationKey"/>.</summary>
    Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct);
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
    IReadOnlyList<WorkflowAction> Actions,
    WorkflowChip? Chip);

public sealed record WorkflowAction(string Id, string Label, string Style, string Effect, string? ResultText);

public sealed record WorkflowChip(string Text, string Tone);

/// <summary>
/// RCU-BGV-008: whether the caller may see sensitive check notes (salary confirmations and the like),
/// from the Identity masking map (resource <c>bgvCheck</c>, field <c>sensitiveNote</c>).
/// </summary>
public interface ISensitiveNotePolicy
{
    Task<bool> CallerMaySeeAsync(CancellationToken ct);
}

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
