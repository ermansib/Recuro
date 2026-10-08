using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Domain.Cases;

/// <summary>BGV check states (FRD §6.3), same names as the frontend's <c>BgvCheckStatus</c>.</summary>
public enum CheckStatus
{
    Pending,
    InProgress,
    Cleared,
    Flagged,
    NotApplicable,
}

/// <summary>
/// Legal check moves as data, identical to <c>bgvCheckTransitions</c> in
/// frontend/src/domain/stateMachines.ts. FRD §6.3's "Under Review → Resolved" lives on the case
/// (<see cref="CaseStatus.UnderReview"/>), so a flagged check only ever moves to Cleared (override).
/// </summary>
public static class CheckTransitions
{
    public static readonly TransitionTable<CheckStatus> Table = new(new Dictionary<CheckStatus, CheckStatus[]>
    {
        [CheckStatus.Pending] = [CheckStatus.InProgress, CheckStatus.NotApplicable],
        [CheckStatus.InProgress] = [CheckStatus.Cleared, CheckStatus.Flagged],
        [CheckStatus.Flagged] = [CheckStatus.Cleared],
        [CheckStatus.Cleared] = [],
        [CheckStatus.NotApplicable] = [],
    });

    /// <summary>A check that no longer blocks the release gate.</summary>
    public static bool IsSettled(CheckStatus status) => status is CheckStatus.Cleared or CheckStatus.NotApplicable;
}

/// <summary>Where the case as a whole stands.</summary>
public enum CaseStatus
{
    /// <summary>Checks are running.</summary>
    Open,

    /// <summary>RCU-BGV-006: an adverse finding is with HR Head + Compliance → MD/CEO. Offer release is locked.</summary>
    UnderReview,

    /// <summary>Every applicable check cleared; <c>bgv.cleared</c> was published.</summary>
    Cleared,

    /// <summary>The adverse finding was upheld and the offer rescinded.</summary>
    Rescinded,

    /// <summary>The application was rejected or withdrawn while the case was running.</summary>
    Cancelled,
}

/// <summary>What HR-TA asks for when reporting an adverse finding (frontend <c>BgvCase.adverse.action</c>).</summary>
public enum AdverseAction
{
    HoldAndEscalate,
    SeekClarification,
}

/// <summary>The final decision on an adverse finding (RCU-BGV-006): override and proceed, or rescind the offer.</summary>
public enum AdverseOutcome
{
    Override,
    Rescind,
}

/// <summary>RCU-PIP-004 / PPL-008: internal (IJP) candidates get a delta-only check scope.</summary>
public enum CheckScope
{
    Full,
    Delta,
}
