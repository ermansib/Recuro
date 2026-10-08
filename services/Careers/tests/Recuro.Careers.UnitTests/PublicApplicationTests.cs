using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Application.Applications;
using Recuro.Careers.Application.Postings;
using Recuro.Careers.Domain.Applications;

namespace Recuro.Careers.UnitTests;

public sealed class PublicApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static PublicApplication Started() =>
        PublicApplication.Start("post-2026-0153", "REQ-2026-0153", "Credit Analyst", null, [new GivenConsent("DataPrivacy", "v1", Now)], Now);

    [Fact]
    public void The_saga_completes_in_order_and_raises_one_event()
    {
        var application = Started();

        Assert.True(application.CandidateRecorded("cand-1", createdHere: true, Now).IsSuccess);
        Assert.True(application.Complete("APP-2026-0042", Now).IsSuccess);

        Assert.Equal(IntakeState.Completed, application.State);
        Assert.Equal("APP-2026-0042", application.AppId);
        Assert.Equal(PublicStage.Received, application.PublicStage);
        Assert.IsType<PublicApplicationCompleted>(Assert.Single(application.DomainEvents));
    }

    [Fact]
    public void An_application_cannot_complete_before_the_candidate_is_recorded()
    {
        var application = Started();

        Assert.Equal("illegal_transition", application.Complete("APP-2026-0042", Now).Error?.Code);
        Assert.Empty(application.DomainEvents);
    }

    [Fact]
    public void A_matched_candidate_is_recorded_as_a_duplicate_and_is_never_compensated()
    {
        var application = Started();
        application.CandidateRecorded("cand-9", createdHere: false, Now);

        Assert.False(application.CandidateCreatedHere);
        Assert.Equal("cand-9", application.DuplicateOf);
    }

    [Theory]
    [InlineData(true, IntakeState.Compensated)]
    [InlineData(false, IntakeState.CompensationFailed)]
    [InlineData(null, IntakeState.Failed)]
    public void A_failed_application_step_records_how_it_was_undone(bool? compensated, IntakeState expected)
    {
        var application = Started();
        application.CandidateRecorded("cand-1", createdHere: true, Now);

        application.Fail("sourcing_locked", compensated, Now);

        Assert.Equal(expected, application.State);
        Assert.Equal("sourcing_locked", application.FailureCode);
    }

    [Theory]
    [InlineData("Sourced", PublicStage.Received)]
    [InlineData("Screened", PublicStage.UnderReview)]
    [InlineData("Hold", PublicStage.UnderReview)]
    [InlineData("Selection", PublicStage.Interview)]
    [InlineData("BGV", PublicStage.Offer)]
    [InlineData("Withdrawn", PublicStage.Decision)]
    [InlineData("SomethingNew", PublicStage.UnderReview)]
    public void Pipeline_stages_map_to_coarse_public_stages(string stage, PublicStage expected) =>
        Assert.Equal(expected, PublicStages.FromPipeline(stage));

    [Fact]
    public void Status_text_matches_the_frontend_mock()
    {
        Assert.Equal("Under HR review — screening call within ≤7 days", StatusText.For("Sourced"));
        Assert.Equal("Interviews in progress", StatusText.For("Interview"));
        Assert.Equal("Closed — thank you for your interest", StatusText.For("Rejected"));
        Assert.Equal("Under review", StatusText.For("Hold"));
    }

    [Fact]
    public void Final_rejection_keeps_the_event_for_the_regret_receipt()
    {
        var application = Started();
        var eventId = Guid.CreateVersion7();

        application.FinallyRejected(eventId, new DateOnly(2026, 10, 8), Now);
        application.RegretDelivered(Now.AddDays(3));
        application.RegretDelivered(Now.AddDays(4));

        Assert.Equal(PublicStage.Decision, application.PublicStage);
        Assert.Equal(eventId, application.FinalRejectedEventId);
        Assert.Equal(Now.AddDays(3), application.RegretDeliveredAt);
    }

    [Fact]
    public void Cursors_round_trip_and_garbage_is_refused()
    {
        var cursor = new SearchCursor(Now, "post-2026-0153");

        Assert.True(JobCursor.TryDecode(JobCursor.Encode(cursor), out var decoded));
        Assert.Equal(cursor, decoded);
        Assert.True(JobCursor.TryDecode(null, out var none));
        Assert.Null(none);
        Assert.False(JobCursor.TryDecode("not-a-cursor!", out _));
        Assert.False(JobCursor.TryDecode("Zm9v", out _));
    }

    [Fact]
    public async Task The_IJP_window_ends_n_working_days_after_unlock_at_the_same_time_of_day()
    {
        var unlockedAt = new DateTimeOffset(2026, 10, 2, 14, 30, 0, TimeSpan.Zero); // a Friday

        var end = await IjpWindow.EndAsync(new WeekdayCalendar(), unlockedAt, 5, CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 14, 30, 0, TimeSpan.Zero), end);
        Assert.Equal(unlockedAt, await IjpWindow.EndAsync(new WeekdayCalendar(), unlockedAt, 0, CancellationToken.None));
    }

    private sealed class WeekdayCalendar : IWorkingDayCalendar
    {
        public Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct)
        {
            var day = from;
            for (var added = 0; added < days;)
            {
                day = day.AddDays(1);
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    added++;
                }
            }

            return Task.FromResult(day);
        }
    }
}
