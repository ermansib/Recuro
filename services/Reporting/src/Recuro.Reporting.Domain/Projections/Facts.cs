using Recuro.BuildingBlocks.Domain;

namespace Recuro.Reporting.Domain.Projections;

/// <summary>
/// The six funnel stages of RCU-RPT-004 / prototype S-15, in order. Pipeline stages map onto them:
/// Interview and Selection count as Interviewed, Offer and Pre-boarding as Offered, Onboarded and
/// Confirmed as Joined.
/// </summary>
public enum FunnelStage
{
    Sourced = 0,
    Screened = 1,
    Interviewed = 2,
    Bgv = 3,
    Offered = 4,
    Joined = 5,
}

public static class FunnelStages
{
    /// <summary>Maps a pipeline stage (frontend spelling) to its funnel stage; side states map to null.</summary>
    public static FunnelStage? FromPipeline(string? stage) => stage switch
    {
        "Sourced" => FunnelStage.Sourced,
        "Screened" => FunnelStage.Screened,
        "Interview" or "Selection" => FunnelStage.Interviewed,
        "BGV" => FunnelStage.Bgv,
        "Offer" or "PreBoarding" => FunnelStage.Offered,
        "Onboarded" or "Confirmed" => FunnelStage.Joined,
        _ => null,
    };
}

/// <summary>
/// Folding helpers. Every projection update is order-independent and idempotent (earliest or latest
/// wins), so a redelivered event, a replay from an offset or a full rebuild gives the same rows.
/// </summary>
internal static class Fold
{
    public static DateTimeOffset? Earliest(DateTimeOffset? current, DateTimeOffset at) =>
        current is { } value && value <= at ? value : at;
}

/// <summary>One application's journey: when it reached each stage and how its offer ended (read model).</summary>
public sealed class ApplicationFact : Entity, ITenantOwned
{
    private ApplicationFact()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string? ReqId { get; private set; }

    /// <summary>Pseudonymous candidate id, cleared when the candidate is purged.</summary>
    public string? CandidateId { get; private set; }

    /// <summary>Source channel as the pipeline spells it (portal, referral, consultant, ijp, ...).</summary>
    public string? Source { get; private set; }

    public DateTimeOffset? CreatedAt { get; private set; }

    public FunnelStage FurthestStage { get; private set; }

    public DateTimeOffset? ScreenedAt { get; private set; }

    public DateTimeOffset? InterviewedAt { get; private set; }

    public DateTimeOffset? BgvAt { get; private set; }

    public DateTimeOffset? OfferedAt { get; private set; }

    public DateTimeOffset? JoinedAt { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    public DateTimeOffset? OfferSentAt { get; private set; }

    public DateTimeOffset? OfferAcceptedAt { get; private set; }

    public DateTimeOffset? OfferDeclinedAt { get; private set; }

    public DateOnly? JoiningDate { get; private set; }

    /// <summary>An accepted offer is a hire for Time-to-Fill, Time-to-Hire, CPH and Source Mix (FRD §12).</summary>
    public bool IsHire => OfferAcceptedAt is not null;

    public static ApplicationFact Start(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return new ApplicationFact { Id = Guid.CreateVersion7(), AppId = appId };
    }

    public void Identify(string? reqId, string? candidateId, string? source)
    {
        ReqId ??= reqId;
        CandidateId ??= candidateId;
        Source ??= string.IsNullOrWhiteSpace(source) ? null : source;
    }

    public void Created(DateTimeOffset at) => CreatedAt = Fold.Earliest(CreatedAt, at);

    public void Reached(FunnelStage stage, DateTimeOffset at)
    {
        if (stage > FurthestStage)
        {
            FurthestStage = stage;
        }

        switch (stage)
        {
            case FunnelStage.Sourced:
                CreatedAt = Fold.Earliest(CreatedAt, at);
                break;
            case FunnelStage.Screened:
                ScreenedAt = Fold.Earliest(ScreenedAt, at);
                break;
            case FunnelStage.Interviewed:
                InterviewedAt = Fold.Earliest(InterviewedAt, at);
                break;
            case FunnelStage.Bgv:
                BgvAt = Fold.Earliest(BgvAt, at);
                break;
            case FunnelStage.Offered:
                OfferedAt = Fold.Earliest(OfferedAt, at);
                break;
            case FunnelStage.Joined:
                JoinedAt = Fold.Earliest(JoinedAt, at);
                break;
        }
    }

    public void Rejected(DateTimeOffset at) => RejectedAt = Fold.Earliest(RejectedAt, at);

    public void Withdrawn(DateTimeOffset at) => WithdrawnAt = Fold.Earliest(WithdrawnAt, at);

    public void OfferSent(DateTimeOffset at)
    {
        OfferSentAt = Fold.Earliest(OfferSentAt, at);
        Reached(FunnelStage.Offered, at);
    }

    public void OfferAccepted(DateTimeOffset at, DateOnly? joiningDate)
    {
        OfferAcceptedAt = Fold.Earliest(OfferAcceptedAt, at);
        JoiningDate ??= joiningDate;
        Reached(FunnelStage.Offered, at);
    }

    /// <summary>Declined or expired: the candidate did not take the offer.</summary>
    public void OfferDeclined(DateTimeOffset at) => OfferDeclinedAt = Fold.Earliest(OfferDeclinedAt, at);

    /// <summary>A withdrawn accepted offer is no longer a hire.</summary>
    public void OfferWithdrawn(DateTimeOffset at)
    {
        if (OfferAcceptedAt is { } accepted && accepted <= at)
        {
            OfferAcceptedAt = null;
            JoiningDate = null;
        }

        WithdrawnAt = Fold.Earliest(WithdrawnAt, at);
    }

    public void ForgetCandidate() => CandidateId = null;
}

/// <summary>One requisition's approval timeline (read model for MRF-TAT and Time-to-Fill).</summary>
public sealed class RequisitionFact : Entity, ITenantOwned
{
    private RequisitionFact()
    {
    }

    public Guid TenantId { get; private set; }

    public string ReqId { get; private set; } = string.Empty;

    /// <summary>Grade as the tenant names it; Config's DOA matrix gives its overall TAT band.</summary>
    public string? Grade { get; private set; }

    public string? BudgetStatus { get; private set; }

    /// <summary>The last submission before approval: a resubmitted MRF's clock starts again.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? RejectedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? SourcingUnlockedAt { get; private set; }

    public static RequisitionFact Start(string reqId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reqId);
        return new RequisitionFact { Id = Guid.CreateVersion7(), ReqId = reqId };
    }

    public void Submitted(string? grade, string? budgetStatus, DateTimeOffset at)
    {
        Grade = grade ?? Grade;
        BudgetStatus = budgetStatus ?? BudgetStatus;
        var beforeApproval = ApprovedAt is not { } approved || at <= approved;
        if (beforeApproval && (SubmittedAt is not { } submitted || at > submitted))
        {
            SubmittedAt = at;
        }
    }

    public void Approved(DateTimeOffset at) => ApprovedAt = Fold.Earliest(ApprovedAt, at);

    public void Rejected(DateTimeOffset at) => RejectedAt = Fold.Earliest(RejectedAt, at);

    public void Cancelled(DateTimeOffset at) => CancelledAt = Fold.Earliest(CancelledAt, at);

    public void SourcingUnlocked(DateTimeOffset at) => SourcingUnlockedAt = Fold.Earliest(SourcingUnlockedAt, at);
}

/// <summary>
/// One interviewer's Annexure B feedback on one interview (read model for Panel Feedback ≤ 48h). It
/// counts once: the first submission decides whether it was within the SLA, and feedback that went
/// overdue without being submitted counts as late.
/// </summary>
public sealed class FeedbackFact : Entity, ITenantOwned
{
    private FeedbackFact()
    {
    }

    public Guid TenantId { get; private set; }

    public string InterviewId { get; private set; } = string.Empty;

    public string InterviewerId { get; private set; } = string.Empty;

    public string? AppId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public bool? WithinSla { get; private set; }

    public DateTimeOffset? OverdueAt { get; private set; }

    /// <summary>When the feedback counts in a period: on submission, or when it went overdue unsubmitted.</summary>
    public DateTimeOffset? CountsAt => SubmittedAt ?? OverdueAt;

    public bool OnTime => WithinSla == true;

    public static FeedbackFact Start(string interviewId, string interviewerId, string? appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interviewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(interviewerId);
        return new FeedbackFact { Id = Guid.CreateVersion7(), InterviewId = interviewId, InterviewerId = interviewerId, AppId = appId };
    }

    public void Submitted(DateTimeOffset at, bool withinSla)
    {
        if (SubmittedAt is { } first && first <= at)
        {
            return;
        }

        SubmittedAt = at;
        WithinSla = withinSla && (OverdueAt is null || at <= OverdueAt);
    }

    public void Overdue(DateTimeOffset at)
    {
        OverdueAt = Fold.Earliest(OverdueAt, at);
        if (SubmittedAt is { } submitted && submitted > OverdueAt)
        {
            WithinSla = false;
        }
    }
}
