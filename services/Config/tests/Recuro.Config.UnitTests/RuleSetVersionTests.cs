using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.UnitTests;

public class RuleSetVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private static RuleSetVersion Draft(DateTimeOffset? effectiveFrom = null) =>
        RuleSetVersion.Propose(MatrixType.Doa, 2, "{}", effectiveFrom ?? Now.AddDays(7), "tighter KMP route", "u-hrta", "Riya", Now);

    [Fact]
    public void A_proposal_starts_as_a_draft_with_no_event()
    {
        var version = Draft();

        Assert.Equal(VersionStatus.Draft, version.Status);
        Assert.Empty(version.DomainEvents);
    }

    [Fact]
    public void The_proposer_cannot_approve_their_own_change()
    {
        var result = Draft().Approve("u-hrta", "Riya", Now);

        Assert.Equal("second_approver_required", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public void A_second_person_activates_it_and_an_event_is_raised()
    {
        var version = Draft();

        Assert.True(version.Approve("u-head", "Kavya", Now).IsSuccess);

        Assert.Equal(VersionStatus.Active, version.Status);
        Assert.Equal("Kavya", version.DecidedByName);
        var activated = Assert.IsType<RuleSetActivatedDomainEvent>(Assert.Single(version.DomainEvents));
        Assert.Equal(Now.AddDays(7), activated.EffectiveFrom);
    }

    [Fact]
    public void Approval_never_backdates_a_version()
    {
        var version = Draft(Now.AddDays(-30));

        version.Approve("u-head", "Kavya", Now);

        Assert.Equal(Now, version.EffectiveFrom);
    }

    [Fact]
    public void Active_and_rejected_versions_are_locked()
    {
        var active = Draft();
        active.Approve("u-head", "Kavya", Now);
        var rejected = Draft();
        rejected.Reject("u-head", "Kavya", "Band labels are out of date", Now);

        Assert.Equal("version_locked", active.Revise("{}", Now, null, "u-hrta").Error!.Code);
        Assert.Equal("version_locked", rejected.Revise("{}", Now, null, "u-hrta").Error!.Code);
        Assert.Equal("illegal_transition", active.Approve("u-other", "X", Now).Error!.Code);
        Assert.Equal("illegal_transition", rejected.Approve("u-other", "X", Now).Error!.Code);
    }

    [Fact]
    public void Only_the_proposer_revises_a_draft()
    {
        var version = Draft();

        Assert.Equal("not_proposer", version.Revise("{\"routes\":[]}", Now, null, "u-other").Error!.Code);
        Assert.True(version.Revise("{\"routes\":[]}", Now.AddDays(1), "v2", "u-hrta").IsSuccess);
        Assert.Equal("v2", version.Note);
    }

    [Fact]
    public void The_seed_is_active_version_one()
    {
        var seed = RuleSetVersion.Seed(MatrixType.Tat, "{}", Now, Now);

        Assert.Equal(1, seed.Number);
        Assert.Equal(VersionStatus.Active, seed.Status);
    }

    [Theory]
    [InlineData("doa", true)]
    [InlineData("DOA", true)]
    [InlineData("calendar", true)]
    [InlineData("payroll", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    public void Matrix_type_keys_parse(string key, bool known) => Assert.Equal(known, MatrixTypes.TryParse(key, out _));
}
