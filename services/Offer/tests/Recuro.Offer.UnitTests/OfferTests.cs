using Recuro.Offer.Application.Offers;
using Recuro.Offer.Domain.Bgv;
using Recuro.Offer.Domain.Offers;
using Recuro.Offer.Domain.Rules;

namespace Recuro.Offer.UnitTests;

public class OfferTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly OfferRules Rules = new(
        "offer-v1",
        [
            new OfferApprovalRule(["E", "M1"], new ApprovalLeg("HR-TA → HR Head", "hrhead"), new ApprovalLeg("HR-TA → HR Head (deviation)", "hrhead")),
            new OfferApprovalRule(["M3", "VP"], new ApprovalLeg("HR-TA → HR Head", "hrhead"), new ApprovalLeg("HR-TA → MD/CEO", "mdceo")),
        ],
        CtcRuleSet.Default,
        5,
        3,
        7);

    private static JobOffer Draft(CtcComponents? components = null) => JobOffer.Create(
        new OfferDraft(
            "APP-1",
            "REQ-1",
            "CAN-1",
            "Sandeep Verma",
            "Senior Manager — Credit",
            "M3",
            "HQ — Mumbai",
            "N. Sharma",
            new DateOnly(2026, 11, 2),
            6,
            components ?? new CtcComponents(17, 2.8m, 1.7m),
            new PayBand(18, 24)),
        "A. Sharma",
        Now);

    private static JobOffer Approved(CtcComponents? components = null)
    {
        var offer = Draft(components);
        offer.Submit(Rules.Route(offer.Grade, offer.Components, offer.Band)!, "A. Sharma", Now);
        offer.AwaitApproval(Guid.NewGuid());
        offer.ApplyDecision(true, "K. Iyer", null, Now);
        return offer;
    }

    private static SendPlan Plan => new(Now.AddDays(3), Now.AddDays(7), TimeSpan.FromDays(7), false);

    [Fact]
    public void Routing_follows_annexure_d()
    {
        var within = Rules.Route("M3", new CtcComponents(17, 2.8m, 1.7m), new PayBand(18, 24))!;
        Assert.True(within.WithinBand);
        Assert.Equal("hrhead", within.ApproverRole);
        Assert.Equal(21.5m, within.Total);

        var deviation = Rules.Route("M3", new CtcComponents(19.6m, 3.2m, 2), new PayBand(18, 24))!;
        Assert.False(deviation.WithinBand);
        Assert.Equal(0.8m, deviation.Deviation);
        Assert.Equal("mdceo", deviation.ApproverRole);

        Assert.Null(Rules.Route("KMP", new CtcComponents(50, 10, 5), new PayBand(40, 60)));
    }

    [Fact]
    public void Ctc_rules_report_each_broken_rule_with_its_id()
    {
        Assert.Empty(CtcRuleSet.Default.Validate(new CtcComponents(17, 2.8m, 1.7m)));
        Assert.Equal(["ctc.non-negative"], CtcRuleSet.Default.Validate(new CtcComponents(-1, 2, 1)).Select(v => v.RuleId));
        Assert.Equal(
            ["ctc.fixed-min", "ctc.variable-max"],
            CtcRuleSet.Default.Validate(new CtcComponents(4, 5, 1)).Select(v => v.RuleId));
    }

    [Fact]
    public void Approval_issues_the_next_letter_version_and_is_idempotent()
    {
        var offer = Approved();

        Assert.Equal(OfferState.Approved, offer.State);
        Assert.Equal(1, offer.LetterVersion);
        Assert.True(offer.ApplyDecision(true, "K. Iyer", null, Now).IsSuccess);
        Assert.Equal(1, offer.LetterVersion);
        Assert.Single(offer.DomainEvents.OfType<OfferApproved>());
    }

    [Fact]
    public void A_declined_approval_returns_the_offer_to_draft()
    {
        var offer = Draft();
        offer.Submit(Rules.Route(offer.Grade, offer.Components, offer.Band)!, "A. Sharma", Now);

        Assert.True(offer.ApplyDecision(false, "K. Iyer", "Band needs Finance review first.", Now).IsSuccess);
        Assert.Equal(OfferState.Draft, offer.State);
        Assert.Null(offer.Route);
        Assert.Contains("Band needs Finance review", offer.Trail[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Revising_an_approved_offer_needs_a_new_approval_and_writes_the_trail()
    {
        var offer = Approved();

        Assert.True(offer.Revise(new CtcComponents(18, 2.8m, 1.7m), "Competing offer", "A. Sharma", Now).IsSuccess);

        Assert.Equal(OfferState.Draft, offer.State);
        Assert.Equal("CTC revised to ₹22.5L (was ₹21.5L)", offer.Trail[0].Title);
        Assert.Contains("Competing offer", offer.Trail[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sent_offer_is_chased_once_per_period_then_expires()
    {
        var offer = Approved();
        Assert.True(offer.Send(Plan, "A. Sharma", Now).IsSuccess);
        offer.ClearDomainEvents();

        Assert.False(offer.RaiseChaseIfDue(Now.AddDays(2)));
        Assert.True(offer.RaiseChaseIfDue(Now.AddDays(3)));
        Assert.False(offer.RaiseChaseIfDue(Now.AddDays(4)));
        Assert.Equal(1, offer.ChaseCount);
        Assert.Equal(Now.AddDays(10), offer.NextChaseAt);

        Assert.False(offer.RaiseChaseIfDue(Now.AddDays(7)));
        Assert.True(offer.ExpireIfDue(Now.AddDays(7)));
        Assert.Equal(OfferState.Expired, offer.State);
        Assert.IsType<OfferExpired>(offer.DomainEvents.Last());
    }

    [Fact]
    public void Withdrawal_needs_a_reason_and_works_even_after_acceptance()
    {
        var offer = Approved();
        offer.Send(Plan, "A. Sharma", Now);
        Assert.True(offer.Accept("A. Sharma", Now).IsSuccess);
        Assert.True(offer.Accept("A. Sharma", Now).IsSuccess);

        Assert.True(offer.Withdraw("short", "K. Iyer", Now).IsFailure);
        Assert.True(offer.Withdraw("Candidate failed the Fit & Proper check.", "K. Iyer", Now).IsSuccess);
        Assert.Equal(OfferState.Withdrawn, offer.State);
        Assert.Equal(OfferState.Accepted, offer.DomainEvents.OfType<OfferWithdrawn>().Single().From);
        Assert.True(offer.Withdraw("Candidate failed the Fit & Proper check.", "K. Iyer", Now).IsFailure);
    }

    [Fact]
    public void Illegal_moves_are_conflicts()
    {
        var offer = Draft();

        Assert.Equal("illegal_transition", offer.Send(Plan, "A. Sharma", Now).Error!.Code);
        Assert.Equal("illegal_transition", offer.Accept("A. Sharma", Now).Error!.Code);
    }

    [Fact]
    public void Bgv_gate_lists_what_blocks_release()
    {
        Assert.Equal(["Background verification has not been initiated"], BgvTrack.BlockersFor(null));

        var track = BgvTrack.Start("APP-1", Now);
        Assert.NotEmpty(BgvTrack.BlockersFor(track));
        track.Apply(BgvGate.Adverse, ["Education"], Now.AddHours(1));
        Assert.Equal(["Adverse finding: Education"], BgvTrack.BlockersFor(track));
        track.Apply(BgvGate.Cleared, [], Now);
        Assert.Equal(BgvGate.Adverse, track.Gate);
        track.Apply(BgvGate.Cleared, [], Now.AddHours(2));
        Assert.Empty(BgvTrack.BlockersFor(track));
    }

    [Fact]
    public void Masking_hides_ctc_and_trail_amounts_for_roles_without_access()
    {
        var dto = OfferDto.From(Approved());
        var hidden = OfferMasking.HiddenFields([OfferMasking.LocalFallback("mdceo")]);
        var masked = OfferMasking.Apply(dto, hidden);

        Assert.Null(masked.Components);
        Assert.Null(masked.Band);
        Assert.Equal("Sandeep Verma", masked.CandidateName);
        Assert.DoesNotContain(masked.Trail, t => t.Title.Contains("21.5", StringComparison.Ordinal));

        var failClosed = OfferMasking.Apply(dto, OfferMasking.HiddenFields([null]));
        Assert.Equal(string.Empty, failClosed.CandidateName);

        var both = OfferMasking.HiddenFields([OfferMasking.LocalFallback("mdceo"), OfferMasking.LocalFallback("hrhead")]);
        Assert.Empty(both);
    }
}
