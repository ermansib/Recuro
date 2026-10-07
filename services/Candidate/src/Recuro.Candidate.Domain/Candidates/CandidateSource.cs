namespace Recuro.Candidate.Domain.Candidates;

/// <summary>Where a candidate came from (FRD §9, frontend <c>CandidateSource</c>). Drives the §12 source mix.</summary>
public enum CandidateSource
{
    Ijp,
    Referral,
    Portal,
    Campus,
    WalkIn,
    Consultant,
    Social,
    LinkedIn,
    Direct,
}

/// <summary>
/// RCU-CND-005: how the candidate was sourced. Stored once at creation and never changed.
/// </summary>
/// <param name="Source">The channel type.</param>
/// <param name="SourceRef">Free reference shown to HR, e.g. the referring employee's code.</param>
/// <param name="ReferrerId">Employee id of the referrer, for referrals.</param>
/// <param name="ConsultantId">Vendor id of the consultant, for consultant-sourced candidates.</param>
/// <param name="Channel">Finer channel, e.g. a job board or campus name.</param>
public sealed record SourceAttribution(
    CandidateSource Source,
    string? SourceRef,
    string? ReferrerId,
    string? ConsultantId,
    string? Channel);
