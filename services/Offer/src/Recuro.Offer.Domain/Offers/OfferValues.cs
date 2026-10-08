namespace Recuro.Offer.Domain.Offers;

/// <summary>CTC breakup in lakhs (frontend <c>Offer.components</c>).</summary>
public sealed record CtcComponents(decimal Fixed, decimal Variable, decimal Benefits)
{
    /// <summary>Annual CTC, one decimal, as the offer screen shows it.</summary>
    public decimal Total => Math.Round(Fixed + Variable + Benefits, 1, MidpointRounding.AwayFromZero);
}

/// <summary>The requisition's pay band in lakhs (frontend <c>Offer.band</c>).</summary>
public sealed record PayBand(decimal Min, decimal Max);

/// <summary>One line of the offer's trail (§9.8): newest first on the screen.</summary>
public sealed record TrailEntry(string Title, string Detail, bool? Approved, DateTimeOffset At, string Kind);

public static class TrailKinds
{
    public const string Created = "created";
    public const string Revision = "revision";
    public const string Verbal = "verbal";
    public const string Submitted = "submitted";
    public const string Decision = "decision";
    public const string Letter = "letter";
    public const string Sent = "sent";
    public const string Response = "response";
    public const string Chase = "chase";
    public const string Withdrawn = "withdrawn";
    public const string Bgv = "bgv";
}

/// <summary>
/// RCU-OFR-002 (Annexure D): band check and approver, from the Config offer matrix version pinned on
/// submit and carried in <c>offer.submitted</c>.
/// </summary>
public sealed record OfferRoute(
    decimal Total,
    decimal Deviation,
    bool WithinBand,
    string ApproverRole,
    string Label,
    string ConfigVersionId);
