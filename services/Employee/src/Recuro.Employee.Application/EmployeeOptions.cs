using Recuro.Employee.Domain.Referrals;

namespace Recuro.Employee.Application;

/// <summary>Bound from <c>Employee</c>.</summary>
public sealed class EmployeeOptions
{
    public const string SectionName = "Employee";

    /// <summary>
    /// FRD §9.2.1: working days an opening is listed internally, counted from the moment sourcing was
    /// unlocked. Moves to the Config service's rules when it carries the IJP policy.
    /// </summary>
    public int IjpWindowWorkingDays { get; set; } = 5;

    /// <summary>Wording version of the privacy notice and COI declaration on the internal forms.</summary>
    public string ConsentTextVersion { get; set; } = "v1";

    /// <summary>RCU-EMP-003: whether the tenant runs a referral bonus scheme.</summary>
    public bool ReferralBonusEnabled { get; set; } = true;

    /// <summary>Relationships that never earn a bonus (FRD §7.1).</summary>
    public IList<ReferralRelationship> ReferralBonusExcluded { get; } = [ReferralRelationship.Relative];

    public ReferralBonusPolicy BonusPolicy => new(ReferralBonusEnabled, ReferralBonusExcluded.ToList());
}
