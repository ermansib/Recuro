using Recuro.BuildingBlocks.Domain;

namespace Recuro.Requisition.Domain.Requisitions;

/// <summary>
/// A Manpower Requisition (MRF): approved demand that unlocks sourcing. The aggregate guards its own
/// lifecycle (FRD §6); handlers orchestrate the calls to Config and Workflow around it.
/// </summary>
public sealed class ManpowerRequisition : AggregateRoot, ITenantOwned
{
    private const string DraftPrefix = "DRAFT-";

    private ManpowerRequisition()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary><c>REQ-YYYY-####</c> once submitted (per-tenant sequence); <c>DRAFT-…</c> before that.</summary>
    public string ReqId { get; private set; } = string.Empty;

    public RequisitionState State { get; private set; }

    public RequisitionDetails Details { get; private set; } = null!;

    public string OwnerId { get; private set; } = string.Empty;

    public string OwnerName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateOnly? TargetClosure { get; private set; }

    public ApprovalRoute Route { get; private set; } = ApprovalRoute.None;

    /// <summary>Rules version pinned at submit (RCU-CFG-003).</summary>
    public string? ConfigVersionId { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    /// <summary>Reason of the last rejection or cancellation.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset StateChangedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin), exposed to clients as the ETag.</summary>
    public uint Version { get; private set; }

    public bool HasIssuedReqId => !ReqId.StartsWith(DraftPrefix, StringComparison.Ordinal);

    public bool SourcingAllowed => RequisitionTransitions.SourcingOpen.Contains(State);

    public static ManpowerRequisition CreateDraft(RequisitionDetails details, string ownerId, string ownerName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var id = Guid.CreateVersion7();
        return new ManpowerRequisition
        {
            Id = id,
            ReqId = $"{DraftPrefix}{id.ToString("N")[^8..].ToUpperInvariant()}",
            State = RequisitionState.Draft,
            Details = details,
            OwnerId = ownerId,
            OwnerName = ownerName,
            CreatedAt = now,
            StateChangedAt = now,
        };
    }

    /// <summary>RCU-REQ-001: autosave. Only drafts change; the caller checks the version first.</summary>
    public Result UpdateDraft(RequisitionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (State != RequisitionState.Draft)
        {
            return RequisitionErrors.NotADraft(ReqId, State);
        }

        Details = details;
        return Result.Success();
    }

    /// <summary>Checks that a submit can start: a complete draft.</summary>
    public Result EnsureCanSubmit()
    {
        if (State != RequisitionState.Draft)
        {
            return RequisitionErrors.NotADraft(ReqId, State);
        }

        var missing = Details.MissingForSubmit();
        return missing.Count == 0 ? Result.Success() : RequisitionErrors.Incomplete(missing);
    }

    /// <summary>RCU-REQ-002: the REQ-ID is issued once and kept, even if a later step of submit fails.</summary>
    public void IssueReqId(string reqId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reqId);
        if (HasIssuedReqId)
        {
            throw new InvalidOperationException($"{ReqId} already has a REQ-ID.");
        }

        ReqId = reqId;
    }

    /// <summary>RCU-REQ-002/004: Draft → PendingApproval with the resolved route, pinned rules and workflow.</summary>
    public Result MarkSubmitted(SubmissionPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ready = EnsureCanSubmit();
        if (ready.IsFailure)
        {
            return ready;
        }

        if (!HasIssuedReqId)
        {
            throw new InvalidOperationException("Issue a REQ-ID before submitting.");
        }

        Route = plan.Route;
        ConfigVersionId = plan.ConfigVersionId;
        TargetClosure = plan.TargetClosure;
        WorkflowInstanceId = plan.WorkflowInstanceId;
        SubmittedAt = now;
        Reason = null;
        MoveTo(RequisitionState.PendingApproval, now);
        Raise(new RequisitionSubmitted(Id, ReqId, Details.Grade, Details.OutOfBudget, plan.WorkflowInstanceId, plan.ConfigVersionId));
        return Result.Success();
    }

    /// <summary>
    /// RCU-REQ-006: the approval workflow finished. Approve → Approved (sourcing unlocks); reject →
    /// Rejected with the reason. A repeated decision for the same outcome is a no-op (events are redelivered).
    /// </summary>
    public Result ApplyApprovalOutcome(bool approved, string? reason, DateTimeOffset now)
    {
        var target = approved ? RequisitionState.Approved : RequisitionState.Rejected;
        if (State == target || (approved && SourcingAllowed))
        {
            return Result.Success();
        }

        var move = RequisitionTransitions.Table.EnsureCanMove(State, target, $"Requisition {ReqId}");
        if (move.IsFailure)
        {
            return move;
        }

        MoveTo(target, now);
        if (approved)
        {
            Raise(new RequisitionApproved(Id, ReqId, TargetClosure, ConfigVersionId ?? string.Empty));
        }
        else
        {
            Reason = reason;
            Raise(new RequisitionRejected(Id, ReqId, reason ?? string.Empty, ConfigVersionId ?? string.Empty));
        }

        return Result.Success();
    }

    /// <summary>RCU-REQ-008: cancel with a documented reason.</summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < RequisitionLimits.MinReasonLength)
        {
            return RequisitionErrors.ReasonRequired("reason");
        }

        var from = State;
        var move = RequisitionTransitions.Table.EnsureCanMove(State, RequisitionState.Cancelled, $"Requisition {ReqId}");
        if (move.IsFailure)
        {
            return move;
        }

        Reason = reason.Trim();
        MoveTo(RequisitionState.Cancelled, now);
        Raise(new RequisitionCancelled(Id, ReqId, from, Reason));
        return Result.Success();
    }

    /// <summary>RCU-REQ-005: the sourcing gate other services call before a candidate enters.</summary>
    public Result EnsureSourcingAllowed() =>
        SourcingAllowed ? Result.Success() : RequisitionErrors.SourcingLocked(ReqId, State);

    private void MoveTo(RequisitionState target, DateTimeOffset now)
    {
        State = target;
        StateChangedAt = now;
    }
}
