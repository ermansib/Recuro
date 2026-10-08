using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Intake;
using Recuro.Employee.Domain.Referrals;

namespace Recuro.Employee.UnitTests;

public sealed class IntakeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private static IjpPosting Posting(int minTenureMonths = 12, params string[] grades) =>
        IjpPosting.Open(
            "REQ-2026-0160",
            new IjpPostingContent("Credit Manager", "Jaipur", "Credit", "M1", grades, minTenureMonths, null),
            Now.AddDays(-1),
            Now.AddDays(6),
            "P. Mehta",
            Now);

    private static readonly ReferralBonusPolicy Bonus = new(true, [ReferralRelationship.Relative]);

    [Fact]
    public void An_opening_is_listed_only_inside_its_window_and_until_withdrawn()
    {
        var posting = Posting();

        Assert.False(posting.IsListedAt(Now.AddDays(-2)));
        Assert.True(posting.IsListedAt(Now));
        Assert.False(posting.IsListedAt(Now.AddDays(7)));

        posting.Withdraw(Now);
        Assert.False(posting.IsListedAt(Now));
    }

    [Theory]
    [InlineData("E4", "2024-01-10", null)]
    [InlineData("e3", "2025-10-05", null)]
    [InlineData("M3", "2024-01-10", "grade_not_eligible")]
    [InlineData("E4", "2025-10-06", "tenure_too_short")]
    [InlineData("E4", "2026-12-01", "invalid_date")]
    public void Eligibility_checks_the_grade_band_and_tenure(string grade, string joinedOn, string? expected)
    {
        var result = Posting(12, "E3", "E4").CheckEligibility(grade, DateOnly.Parse(joinedOn, System.Globalization.CultureInfo.InvariantCulture), Today);

        Assert.Equal(expected, result.IsSuccess ? null : result.Error!.Fields[0].Code);
    }

    [Fact]
    public void An_empty_band_admits_every_grade()
    {
        Assert.True(Posting(0).CheckEligibility("Any", Today, Today).IsSuccess);
    }

    [Theory]
    [InlineData("2025-10-05", 12)]
    [InlineData("2025-10-06", 11)]
    [InlineData("2026-09-30", 0)]
    public void Tenure_counts_whole_months(string joinedOn, int months)
    {
        Assert.Equal(months, IjpPosting.TenureMonths(DateOnly.Parse(joinedOn, System.Globalization.CultureInfo.InvariantCulture), Today));
    }

    [Fact]
    public void An_application_completes_in_order_and_raises_one_event()
    {
        var application = InternalApplication.Start("emp-1", "Ravi Kumar", Posting(), "E4", Today.AddYears(-2), Now);

        Assert.Equal("illegal_transition", application.Complete("APP-2026-0042", Now).Error?.Code);
        Assert.True(application.CandidateRecorded("cand-1", createdHere: false, Now).IsSuccess);
        Assert.True(application.Complete("APP-2026-0042", Now).IsSuccess);

        Assert.Equal(IntakeState.Completed, application.State);
        Assert.IsType<InternalApplicationCompleted>(Assert.Single(application.DomainEvents));
        Assert.True(application.IsActive);
    }

    [Theory]
    [InlineData("Interview", ProgressStage.Interview)]
    [InlineData("BGV", ProgressStage.Offer)]
    [InlineData("Confirmed", ProgressStage.Joined)]
    [InlineData("Rejected", ProgressStage.Closed)]
    [InlineData("SomethingNew", ProgressStage.Screening)]
    public void Pipeline_stages_map_to_coarse_progress(string stage, ProgressStage expected)
    {
        Assert.Equal(expected, ProgressStages.FromPipeline(stage));
    }

    [Fact]
    public void A_closed_application_no_longer_blocks_a_new_one()
    {
        var application = InternalApplication.Start("emp-1", "Ravi Kumar", Posting(), "E4", Today.AddYears(-2), Now);
        application.CandidateRecorded("cand-1", createdHere: true, Now);
        application.Complete("APP-2026-0042", Now);

        application.StageChanged("Rejected", Now);

        Assert.False(application.IsActive);
    }

    [Fact]
    public void A_failed_intake_records_whether_the_candidate_was_tombstoned()
    {
        var compensated = InternalApplication.Start("emp-1", "Ravi Kumar", Posting(), "E4", Today.AddYears(-2), Now);
        compensated.CandidateRecorded("cand-1", createdHere: true, Now);
        compensated.Fail("sourcing_locked", compensated: true, Now);

        var notFound = InternalApplication.Start("emp-1", "Ravi Kumar", Posting(), "E4", Today.AddYears(-2), Now);
        notFound.Fail("dependency_unavailable", compensated: null, Now);

        Assert.Equal(IntakeState.Compensated, compensated.State);
        Assert.Equal(IntakeState.Failed, notFound.State);
        Assert.False(compensated.IsActive);
        Assert.Empty(compensated.DomainEvents);
    }

    [Fact]
    public void A_referral_needs_the_coi_declaration()
    {
        var refused = Referral.Submit("emp-1", "Ravi Kumar", "REQ-2026-0160", "Neha Joshi", ReferralRelationship.Friend, coiAccepted: false, Bonus, Now);

        Assert.Equal("coiAccepted", refused.Error?.Fields[0].Field);
    }

    [Theory]
    [InlineData(ReferralRelationship.FormerColleague, true, true)]
    [InlineData(ReferralRelationship.Relative, true, false)]
    [InlineData(ReferralRelationship.Friend, false, false)]
    public void Bonus_eligibility_follows_the_tenant_policy(ReferralRelationship relationship, bool schemeOn, bool eligible)
    {
        var referral = Referral.Submit("emp-1", "Ravi Kumar", "REQ-2026-0160", "Neha Joshi", relationship, true, Bonus with { Enabled = schemeOn }, Now).Value;

        Assert.Equal(eligible, referral.BonusEligible);
    }

    [Fact]
    public void Referring_someone_already_on_file_records_the_match_and_drops_the_bonus()
    {
        var referral = Referral.Submit("emp-1", "Ravi Kumar", "REQ-2026-0160", "Neha Joshi", ReferralRelationship.Friend, true, Bonus, Now).Value;

        referral.MatchedExisting("cand-9");

        Assert.Equal("cand-9", referral.DuplicateOf);
        Assert.False(referral.BonusEligible);
    }
}
