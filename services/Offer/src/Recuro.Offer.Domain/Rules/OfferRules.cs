using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Domain.Rules;

/// <summary>Who approves an offer leg (Config offer matrix <c>withinBand</c> / <c>deviation</c>).</summary>
public sealed record ApprovalLeg(string Label, string ApproverRole);

/// <summary>One Annexure D row: grades and their within-band and deviation approvers.</summary>
public sealed record OfferApprovalRule(IReadOnlyList<string> Levels, ApprovalLeg WithinBand, ApprovalLeg Deviation);

/// <summary>
/// The offer rules this service applies, from the Config offer matrix: approval routing (RCU-OFR-002),
/// the CTC rule-set (RCU-OFR-001) and the acceptance lifecycle timings (RCU-OFR-006).
/// </summary>
public sealed record OfferRules(
    string ConfigVersionId,
    IReadOnlyList<OfferApprovalRule> Approvals,
    CtcRuleSet CtcRules,
    int ValidityWorkingDays,
    int FirstChaseAfterWorkingDays,
    int ChaseEveryDays)
{
    /// <summary>FRD defaults for the lifecycle: 5 working days to sign, first chase after 3, then weekly.</summary>
    public const int DefaultValidityWorkingDays = 5;
    public const int DefaultFirstChaseAfterWorkingDays = 3;
    public const int DefaultChaseEveryDays = 7;

    /// <summary>Annexure D: null when the matrix has no row for the grade.</summary>
    public OfferRoute? Route(string grade, CtcComponents components, PayBand band)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(band);
        var rule = Approvals.FirstOrDefault(r => r.Levels.Contains(grade, StringComparer.Ordinal));
        if (rule is null)
        {
            return null;
        }

        var total = components.Total;
        var deviation = Math.Round(total - band.Max, 1, MidpointRounding.AwayFromZero);
        var withinBand = deviation <= 0;
        var leg = withinBand ? rule.WithinBand : rule.Deviation;
        return new OfferRoute(total, deviation, withinBand, leg.ApproverRole, leg.Label, ConfigVersionId);
    }
}
