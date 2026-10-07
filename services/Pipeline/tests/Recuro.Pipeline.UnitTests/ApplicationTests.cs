using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.UnitTests;

public sealed class ApplicationTests
{
    // Monday 5 October 2026, 09:00 UTC.
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly StageActor HrTa = new("u-1", "A. Sharma", "hrta");

    private static Application NewApplication() =>
        Application.Create("APP-2026-0001", "MRF-2026-0001", "c-1", "Portal", string.Empty, HrTa, Monday);

    [Fact]
    public void A_new_application_starts_at_Sourced_with_one_history_line()
    {
        var application = NewApplication();

        Assert.Equal(ApplicationStage.Sourced, application.Stage);
        var first = Assert.Single(application.StageHistory);
        Assert.Null(first.From);
        Assert.Equal(ApplicationStage.Sourced, first.To);
        Assert.Single(application.DomainEvents.OfType<ApplicationCreatedDomainEvent>());
    }

    [Fact]
    public void The_transition_table_matches_the_frontend_state_machine()
    {
        // frontend/src/domain/stateMachines.ts applicationTransitions, line for line.
        var expected = new Dictionary<ApplicationStage, ApplicationStage[]>
        {
            [ApplicationStage.Sourced] = [ApplicationStage.Screened, ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold],
            [ApplicationStage.Screened] = [ApplicationStage.Interview, ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold],
            [ApplicationStage.Interview] = [ApplicationStage.Selection, ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold],
            [ApplicationStage.Selection] = [ApplicationStage.BGV, ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold],
            [ApplicationStage.BGV] = [ApplicationStage.Offer, ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold],
            [ApplicationStage.Offer] = [ApplicationStage.PreBoarding, ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold],
            [ApplicationStage.PreBoarding] = [ApplicationStage.Onboarded, ApplicationStage.Withdrawn],
            [ApplicationStage.Onboarded] = [ApplicationStage.Confirmed],
            [ApplicationStage.Confirmed] = [],
            [ApplicationStage.Rejected] = [],
            [ApplicationStage.Withdrawn] = [],
            [ApplicationStage.Hold] =
            [
                ApplicationStage.Sourced, ApplicationStage.Screened, ApplicationStage.Interview, ApplicationStage.Selection,
                ApplicationStage.BGV, ApplicationStage.Offer, ApplicationStage.Rejected, ApplicationStage.Withdrawn,
            ],
        };

        foreach (var stage in Enum.GetValues<ApplicationStage>())
        {
            Assert.Equal(expected[stage].Order(), ApplicationTransitions.Table.AllowedFrom(stage).Order());
        }
    }

    [Fact]
    public void An_illegal_move_is_a_conflict_and_changes_nothing()
    {
        var application = NewApplication();

        var result = application.MoveTo(ApplicationStage.Offer, HrTa, Monday);

        Assert.True(result.IsFailure);
        Assert.Equal(ApplicationStage.Sourced, application.Stage);
        Assert.Single(application.StageHistory);
    }

    [Fact]
    public void Rejecting_through_a_plain_move_is_refused()
    {
        var result = NewApplication().MoveTo(ApplicationStage.Rejected, HrTa, Monday);

        Assert.Equal(ApplicationErrors.UseReject.Code, result.Error!.Code);
    }

    [Fact]
    public void Rejection_needs_a_reason_and_sets_the_regret_and_retention_dates()
    {
        var application = NewApplication();
        Assert.Equal(ApplicationErrors.ReasonRequired.Code, application.Reject("  ", HrTa, Monday, new DateOnly(2026, 10, 8), 365).Error!.Code);

        var friday = Monday.AddDays(4);
        Assert.True(application.Reject("Skills gap", HrTa, friday, new DateOnly(2026, 10, 14), 365).IsSuccess);

        Assert.Equal(ApplicationStage.Rejected, application.Stage);
        Assert.Equal(new DateOnly(2026, 10, 14), application.Rejection!.RegretDueBy);
        Assert.Equal(new DateOnly(2027, 10, 9), application.Rejection.RetainUntil);
        Assert.Single(application.DomainEvents.OfType<FinalRejectedDomainEvent>());
    }

    [Fact]
    public void Hold_freezes_the_stage_clock_and_resuming_the_same_stage_continues_it()
    {
        var application = NewApplication();
        application.MoveTo(ApplicationStage.Screened, HrTa, Monday);
        application.MoveTo(ApplicationStage.Hold, HrTa, Monday.AddDays(1));

        application.MoveTo(ApplicationStage.Screened, HrTa, Monday.AddDays(4));

        // One day in Screened before Hold, so the resumed clock started one day before the resume.
        Assert.Equal(Monday.AddDays(3), application.StageEnteredAt);
    }

    [Fact]
    public void A_TAT_breach_is_raised_once_per_stage_and_not_while_on_hold()
    {
        var application = NewApplication();
        var due = WorkingDays.Add(Monday, 7);

        Assert.False(application.FlagTatBreach(due, due));
        Assert.True(application.FlagTatBreach(due, due.AddMinutes(1)));
        Assert.False(application.FlagTatBreach(due, due.AddHours(1)));

        application.MoveTo(ApplicationStage.Screened, HrTa, due.AddHours(2));
        Assert.Null(application.TatBreachedAt);
        application.MoveTo(ApplicationStage.Hold, HrTa, due.AddHours(3));
        Assert.False(application.FlagTatBreach(due, due.AddDays(30)));
    }

    [Fact]
    public void Progress_events_only_advance_from_their_own_stage()
    {
        var application = NewApplication();

        Assert.True(application.Advance(ApplicationStage.Interview, ApplicationStage.Selection, HrTa, Monday).IsSuccess);

        Assert.Equal(ApplicationStage.Sourced, application.Stage);
    }

    [Theory]
    [InlineData("2026-10-02", 1, "2026-10-05")] // Friday + 1 = Monday
    [InlineData("2026-10-05", 5, "2026-10-12")] // Monday + 5 = next Monday
    [InlineData("2026-10-10", 0, "2026-10-10")]
    public void Working_days_skip_weekends(string from, int days, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), WorkingDays.Add(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), days));
}
