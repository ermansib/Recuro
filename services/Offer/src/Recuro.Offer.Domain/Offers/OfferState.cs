using Recuro.BuildingBlocks.Domain;

namespace Recuro.Offer.Domain.Offers;

/// <summary>FRD §6 offer state machine (frontend <c>OfferState</c>).</summary>
public enum OfferState
{
    Draft,
    PendingApproval,
    Approved,
    Sent,
    Accepted,
    Declined,
    Withdrawn,
    Expired,
}

/// <summary>Allowed moves, as data so the frontend mock and the backend share one table.</summary>
public static class OfferTransitions
{
    public static TransitionTable<OfferState> Table { get; } = new(new Dictionary<OfferState, OfferState[]>
    {
        [OfferState.Draft] = [OfferState.PendingApproval, OfferState.Withdrawn],
        [OfferState.PendingApproval] = [OfferState.Approved, OfferState.Draft, OfferState.Withdrawn],
        [OfferState.Approved] = [OfferState.Sent, OfferState.Draft, OfferState.Withdrawn],
        [OfferState.Sent] = [OfferState.Accepted, OfferState.Declined, OfferState.Expired, OfferState.Withdrawn],

        // A rescinded acceptance (RCU-OFR-006: withdrawal with HR Head and a reason).
        [OfferState.Accepted] = [OfferState.Withdrawn],
        [OfferState.Declined] = [],
        [OfferState.Withdrawn] = [],
        [OfferState.Expired] = [],
    });

    /// <summary>States in which the CTC can still be revised; a revision after approval needs a new approval.</summary>
    public static readonly IReadOnlySet<OfferState> Editable = new HashSet<OfferState> { OfferState.Draft, OfferState.PendingApproval, OfferState.Approved };

    /// <summary>The offer is waiting for someone: counted in the dashboard's "Offers Pending" tile.</summary>
    public static readonly IReadOnlySet<OfferState> Pending = new HashSet<OfferState> { OfferState.PendingApproval, OfferState.Approved, OfferState.Sent };

    public static readonly IReadOnlySet<OfferState> Terminal = new HashSet<OfferState> { OfferState.Declined, OfferState.Withdrawn, OfferState.Expired };
}

public static class OfferLimits
{
    public const int IdLength = 64;
    public const int ShortText = 200;
    public const int LongText = 2000;
    public const int MinReasonLength = 10;
    public const int MaxProbationMonths = 24;

    /// <summary>Amounts are in lakhs with one decimal, as on the offer screen.</summary>
    public const decimal MaxAmount = 100_000m;
}
