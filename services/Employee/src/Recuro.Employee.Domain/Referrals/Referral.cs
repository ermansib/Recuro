using Recuro.BuildingBlocks.Domain;
using Recuro.Employee.Domain.Intake;

namespace Recuro.Employee.Domain.Referrals;

/// <summary>How the referrer knows the person (FRD §7.1). Relatives are allowed but never earn a bonus.</summary>
public enum ReferralRelationship
{
    FormerColleague,
    Friend,
    Acquaintance,
    Relative,
    Other,
}

/// <summary>
/// RCU-EMP-003: an employee refers someone for a requisition. The §7.1 conflict-of-interest declaration
/// is mandatory; the referral is stored with the referrer and whether it can earn the bonus, then routed to
/// HR-TA as a Referral-sourced application. Only the referred person's name is kept here; their contact
/// details live in the Candidate service.
/// </summary>
public sealed class Referral : IntakeRecord
{
    private Referral()
    {
    }

    public string CandidateName { get; private set; } = string.Empty;

    public ReferralRelationship Relationship { get; private set; }

    /// <summary>The referrer accepted the §7.1 declaration (always true for a stored referral).</summary>
    public bool CoiAccepted { get; private set; }

    public DateTimeOffset CoiAcceptedAt { get; private set; }

    public bool BonusEligible { get; private set; }

    /// <summary>RCU-EMP-004 (P2): set when the payroll milestone is reached; never set yet.</summary>
    public bool BonusPayable { get; private set; }

    /// <summary>The existing candidate the referral matched (RCU-CND-002). For HR only.</summary>
    public string? DuplicateOf { get; private set; }

    public static Result<Referral> Submit(
        string referrerId,
        string referrerName,
        string reqId,
        string candidateName,
        ReferralRelationship relationship,
        bool coiAccepted,
        ReferralBonusPolicy bonus,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bonus);
        if (!coiAccepted)
        {
            return ReferralErrors.CoiRequired;
        }

        var referral = new Referral
        {
            EmployeeId = referrerId,
            EmployeeName = referrerName,
            ReqId = reqId,
            CandidateName = candidateName.Trim(),
            Relationship = relationship,
            CoiAccepted = true,
            CoiAcceptedAt = now,
            BonusEligible = bonus.IsEligible(relationship),
        };
        referral.Begin(now);
        return referral;
    }

    /// <summary>Records a match with someone already on file; a referral of a known person earns no bonus.</summary>
    public void MatchedExisting(string candidateId) => (DuplicateOf, BonusEligible) = (candidateId, false);

    protected override void OnCompleted() => Raise(new ReferralCompleted(this));
}

/// <summary>Who can earn a referral bonus. Tenant policy, from configuration.</summary>
public sealed record ReferralBonusPolicy(bool Enabled, IReadOnlyCollection<ReferralRelationship> Excluded)
{
    public bool IsEligible(ReferralRelationship relationship) => Enabled && !Excluded.Contains(relationship);
}

/// <summary>The saga finished: publish <c>employee.referral.submitted.v1</c> in the same transaction.</summary>
public sealed record ReferralCompleted(Referral Referral) : IDomainEvent;

public static class ReferralErrors
{
    /// <summary>RCU-EMP-003 says 422; Recuro returns every validation failure as 400 with field errors (frontend contract).</summary>
    public static readonly Error CoiRequired =
        Error.Validation([new FieldError("coiAccepted", "consent_required", "Accept the conflict-of-interest declaration to refer someone (FRD §7.1).")]);

    public static readonly Error SelfReferral =
        Error.Validation([new FieldError("email", "self_referral", "You cannot refer yourself. Apply through IJP instead.")]);

    public static Error AlreadyReferred(string reqId) =>
        Error.Conflict("already_referred", $"You have already referred this person for {reqId}.");
}
