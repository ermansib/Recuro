using Recuro.Offer.Domain.Applications;
using Recuro.Offer.Domain.Bgv;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;
using Recuro.Offer.Domain.Rules;

namespace Recuro.Offer.Application.Abstractions;

/// <summary>Offers of the current tenant. Never returns another tenant's rows.</summary>
public interface IOfferRepository
{
    Task<JobOffer?> GetAsync(Guid id, CancellationToken ct);

    Task<JobOffer?> GetByWorkflowAsync(Guid workflowInstanceId, CancellationToken ct);

    /// <summary>Offers of an application, newest first.</summary>
    Task<IReadOnlyList<JobOffer>> ListForApplicationAsync(string appId, CancellationToken ct);

    Task<IReadOnlyList<JobOffer>> ListAsync(OfferState? state, int limit, CancellationToken ct);

    /// <summary>Sent offers whose chase or expiry time has passed.</summary>
    Task<IReadOnlyList<JobOffer>> ListLifecycleDueAsync(DateTimeOffset now, int limit, CancellationToken ct);

    Task<int> CountAsync(IReadOnlyCollection<OfferState> states, CancellationToken ct);

    Task<IReadOnlyList<JobOffer>> ListForCandidateAsync(string candidateId, CancellationToken ct);

    void Add(JobOffer offer);
}

public interface IApplicationTrackRepository
{
    Task<ApplicationTrack?> GetAsync(string appId, CancellationToken ct);

    void Add(ApplicationTrack track);
}

public interface IBgvTrackRepository
{
    Task<BgvTrack?> GetAsync(string appId, CancellationToken ct);

    void Add(BgvTrack track);
}

public interface IOfferDocumentRepository
{
    /// <summary>The newest document of a kind for an offer (latest letter version), or null.</summary>
    Task<OfferDocument?> GetLatestAsync(Guid offerId, OfferDocumentKind kind, CancellationToken ct);

    void Add(OfferDocument document);
}

/// <summary>
/// The offer rules from the Config service's offer matrix (RCU-CFG-001). Routing never falls back to a
/// guess: an unreachable Config is a <see cref="DependencyUnavailableException"/>.
/// </summary>
public interface IOfferRulesSource
{
    Task<OfferRules> GetAsync(CancellationToken ct);
}

/// <summary>Config's business calendar (<c>GET /api/v1/resolve/working-days</c>).</summary>
public interface IWorkingDays
{
    Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct);
}

/// <summary>The requisition fields an offer copies (Requisition <c>GET /api/v1/requisitions/{reqId}</c>).</summary>
public interface IRequisitions
{
    /// <summary>Null when the requisition doesn't exist for the tenant.</summary>
    Task<RequisitionView?> GetAsync(string reqId, CancellationToken ct);
}

public sealed record RequisitionView(string ReqId, string Designation, string Grade, string Location, string ReportingManager);

/// <summary>RCU-AUT-004 masking map for the <c>offer</c> resource and one role. Null means the role has no map (fail closed).</summary>
public interface IMaskingMaps
{
    Task<IReadOnlyDictionary<string, string>?> GetAsync(string role, CancellationToken ct);
}

/// <summary>Renders the offer letter (RCU-OFR-004). The Infrastructure layer writes the PDF.</summary>
public interface ILetterRenderer
{
    byte[] Render(JobOffer offer);
}

/// <summary>Short-lived signed links to a letter (RCU-OFR-004 "presigned URL"), verifiable without a session.</summary>
public interface ILetterLinks
{
    LetterLink Create(Guid tenantId, Guid offerId, int letterVersion, DateTimeOffset expiresAt);

    bool Verify(Guid tenantId, Guid offerId, int letterVersion, long expiresUnix, string signature, DateTimeOffset now);
}

public sealed record LetterLink(string Url, DateTimeOffset ExpiresAt);

/// <summary>E-sign adapter (RCU-OFR-004). The default logs; a tenant plugs in its e-sign provider.</summary>
public interface IESignGateway
{
    /// <summary>Opens an envelope for the candidate and returns its id. Idempotent on offer id + letter version.</summary>
    Task<string> SendAsync(ESignEnvelope envelope, CancellationToken ct);
}

public sealed record ESignEnvelope(Guid OfferId, int LetterVersion, string CandidateId, string DocumentUrl, DateTimeOffset ExpiresAt);

/// <summary>The Workflow service's instance and approval APIs (RCU-WFL-001/002).</summary>
public interface IWorkflowClient
{
    Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct);

    Task CancelAsync(Guid instanceId, string reason, CancellationToken ct);

    /// <summary>The instance with its tasks, or null when it doesn't exist.</summary>
    Task<WorkflowInstanceView?> GetAsync(Guid instanceId, CancellationToken ct);

    /// <summary>Decides a task as the caller. Workflow enforces that the caller is the assignee.</summary>
    Task<WorkflowDecisionResult> DecideAsync(Guid taskId, string actionId, string? reason, CancellationToken ct);
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
    WorkflowSensitive? Sensitive,
    WorkflowChip? Chip,
    IReadOnlyList<WorkflowAction> Actions);

public sealed record WorkflowSensitive(string Label, string Value);

public sealed record WorkflowChip(string Text, string Tone);

public sealed record WorkflowAction(string Id, string Label, string Style, string Effect, string? ResultText);

public sealed record WorkflowInstanceView(Guid Id, string Status, IReadOnlyList<WorkflowTaskView> Tasks);

public sealed record WorkflowTaskView(Guid Id, string AssigneeRole, string Status);

/// <summary>Outcome of a decision call: ok, or the HTTP status Workflow refused it with.</summary>
public sealed record WorkflowDecisionResult(bool Accepted, int StatusCode, string? Detail);

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
