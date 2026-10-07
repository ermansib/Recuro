using Recuro.BuildingBlocks.Domain;

namespace Recuro.Requisition.Domain.Requisitions;

/// <summary>FRD §6 requisition lifecycle. Names match the frontend's <c>RequisitionState</c>.</summary>
public enum RequisitionState
{
    Draft,
    PendingApproval,
    Approved,
    Sourcing,
    Interviewing,
    Selection,
    BGV,
    Offer,
    Filled,
    Rejected,
    OnHold,
    Cancelled,
}

public enum EmploymentType
{
    Permanent,
    Contractual,
    OffRoll,
}

public enum RequisitionNature
{
    NewPosition,
    Replacement,
    Backfill,
}

/// <summary>
/// Legal moves, as data. The same table as <c>requisitionTransitions</c> in
/// <c>frontend/src/domain/stateMachines.ts</c>.
/// </summary>
public static class RequisitionTransitions
{
    public static TransitionTable<RequisitionState> Table { get; } = new(new Dictionary<RequisitionState, RequisitionState[]>
    {
        [RequisitionState.Draft] = [RequisitionState.PendingApproval, RequisitionState.Cancelled],
        [RequisitionState.PendingApproval] = [RequisitionState.Approved, RequisitionState.Rejected, RequisitionState.Draft],
        [RequisitionState.Approved] = [RequisitionState.Sourcing, RequisitionState.OnHold, RequisitionState.Cancelled],
        [RequisitionState.Sourcing] = [RequisitionState.Interviewing, RequisitionState.OnHold, RequisitionState.Cancelled],
        [RequisitionState.Interviewing] = [RequisitionState.Selection, RequisitionState.Sourcing, RequisitionState.OnHold, RequisitionState.Cancelled],
        [RequisitionState.Selection] = [RequisitionState.BGV, RequisitionState.Interviewing, RequisitionState.OnHold, RequisitionState.Cancelled],
        [RequisitionState.BGV] = [RequisitionState.Offer, RequisitionState.Selection, RequisitionState.OnHold, RequisitionState.Cancelled],
        [RequisitionState.Offer] = [RequisitionState.Filled, RequisitionState.Sourcing, RequisitionState.OnHold, RequisitionState.Cancelled],
        [RequisitionState.Filled] = [],
        [RequisitionState.Rejected] = [RequisitionState.Draft],
        [RequisitionState.OnHold] =
        [
            RequisitionState.Approved, RequisitionState.Sourcing, RequisitionState.Interviewing, RequisitionState.Selection,
            RequisitionState.BGV, RequisitionState.Offer, RequisitionState.Cancelled,
        ],
        [RequisitionState.Cancelled] = [],
    });

    /// <summary>RCU-MRF-006 / RCU-REQ-005: candidates may enter only in these states (same list as the mock's SOURCING_OPEN).</summary>
    public static IReadOnlySet<RequisitionState> SourcingOpen { get; } = new HashSet<RequisitionState>
    {
        RequisitionState.Approved,
        RequisitionState.Sourcing,
        RequisitionState.Interviewing,
        RequisitionState.Selection,
        RequisitionState.BGV,
        RequisitionState.Offer,
    };
}
