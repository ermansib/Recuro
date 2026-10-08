using Recuro.BuildingBlocks.Domain;
using Recuro.Careers.Domain.Postings;

namespace Recuro.Careers.UnitTests;

public sealed class JobPostingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly PostingActor HrTa = new("u-1", "A. Sharma");

    private static readonly PostingContent Content = new(
        "Credit Analyst — Affordable Housing", "Branch Jaipur", "Jaipur", "2–4 yrs", "MBA-Finance", "Housing Finance", [new PostingTag("Full-time", "navy")]);

    private static JobPosting Draft() => JobPosting.Draft("REQ-2026-0153", Content, HrTa, Now);

    [Fact]
    public void A_new_posting_is_a_draft_with_a_public_id_derived_from_the_requisition()
    {
        var posting = Draft();

        Assert.Equal(PostingStatus.Draft, posting.Status);
        Assert.Equal("post-2026-0153", posting.PostingId);
        Assert.False(posting.IsVisibleAt(Now));
        Assert.Equal(PostingAction.Drafted, Assert.Single(posting.History).Action);
    }

    [Fact]
    public void Publishing_needs_sourcing_to_be_unlocked()
    {
        var posting = Draft();

        var result = posting.Publish(sourcingOpen: false, Now, early: null, HrTa, Now);

        Assert.Equal("sourcing_locked", result.Error?.Code);
        Assert.Equal(PostingStatus.Draft, posting.Status);
    }

    [Fact]
    public void While_the_IJP_window_runs_the_public_sees_the_posting_only_after_it()
    {
        var posting = Draft();
        var windowEnd = Now.AddDays(5);

        Assert.True(posting.Publish(sourcingOpen: true, windowEnd, early: null, HrTa, Now).IsSuccess);

        Assert.Equal(PostingStatus.Published, posting.Status);
        Assert.Equal(windowEnd, posting.VisibleFrom);
        Assert.False(posting.IsVisibleAt(Now));
        Assert.True(posting.IsVisibleAt(windowEnd));
    }

    [Fact]
    public void An_early_release_needs_a_justification_and_is_logged()
    {
        var posting = Draft();
        var windowEnd = Now.AddDays(5);

        var missing = posting.Publish(sourcingOpen: true, windowEnd, new EarlyRelease(" "), HrTa, Now);
        Assert.Equal(ErrorType.Validation, missing.Error?.Type);

        Assert.True(posting.Publish(sourcingOpen: true, windowEnd, new EarlyRelease("Niche skill, no internal pool"), HrTa, Now).IsSuccess);
        Assert.True(posting.IsVisibleAt(Now));
        var logged = posting.History[^1];
        Assert.Equal(PostingAction.PublishedEarly, logged.Action);
        Assert.Equal("Niche skill, no internal pool", logged.Reason);
    }

    [Fact]
    public void An_early_release_after_the_window_is_an_ordinary_publish()
    {
        var posting = Draft();

        Assert.True(posting.Publish(sourcingOpen: true, Now.AddDays(-1), new EarlyRelease(string.Empty), HrTa, Now).IsSuccess);

        Assert.Equal(Now, posting.VisibleFrom);
        Assert.Equal(PostingAction.Published, posting.History[^1].Action);
    }

    [Fact]
    public void Unpublishing_needs_a_reason_and_hides_the_posting()
    {
        var posting = Draft();
        posting.Publish(sourcingOpen: true, Now, early: null, HrTa, Now);

        Assert.Equal(ErrorType.Validation, posting.Unpublish("", HrTa, Now).Error?.Type);
        Assert.True(posting.Unpublish("Hiring paused", HrTa, Now).IsSuccess);

        Assert.False(posting.IsVisibleAt(Now.AddDays(10)));
        Assert.Equal("Hiring paused", posting.History[^1].Reason);
    }

    [Fact]
    public void A_closed_posting_is_frozen()
    {
        var posting = Draft();
        posting.Close("Requisition cancelled", HrTa, Now);
        posting.Close("again", HrTa, Now);

        Assert.Equal(PostingStatus.Closed, posting.Status);
        Assert.Equal("illegal_transition", posting.Publish(sourcingOpen: true, Now, early: null, HrTa, Now).Error?.Code);
        Assert.Equal("posting_closed", posting.Edit(Content, HrTa, Now).Error?.Code);
        Assert.Single(posting.History, h => h.Action == PostingAction.Closed);
    }

    [Fact]
    public void The_transition_table_matches_the_story()
    {
        Assert.True(PostingTransitions.Table.CanMove(PostingStatus.Unpublished, PostingStatus.Published));
        Assert.False(PostingTransitions.Table.CanMove(PostingStatus.Draft, PostingStatus.Unpublished));
        Assert.Empty(PostingTransitions.Table.AllowedFrom(PostingStatus.Closed));
    }
}
