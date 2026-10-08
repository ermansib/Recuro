using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Rules;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.UnitTests;

public class InterviewRoundTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Now.AddDays(1);
    private static readonly string[] Competencies = ["credit", "risk", "leadership"];

    private static InterviewRound Round(params PanelMember[] panel) =>
        InterviewRound.Schedule(
            new RoundPlan(
                "APP-1",
                "REQ-2026-0001",
                "CAN-1",
                "M3",
                "functional",
                "Functional Interview",
                2,
                InterviewMode.Video,
                Start,
                60,
                panel.Length == 0 ? [new PanelMember("u-p1", "P. Rao")] : panel,
                Competencies,
                ["case-study.pdf"],
                InterviewRules.FrdDefault.Sla),
            "u-ta",
            Now);

    private static AssessmentInput Complete(Recommendation recommendation = Recommendation.Recommend) => new(
        [new Rating("credit", 4, false, "Solid"), new Rating("risk", 3, false, "Fair"), new Rating("leadership", null, true, string.Empty)],
        recommendation,
        "Strong on credit appraisal; risk depth is adequate.",
        ["verify campus claims"]);

    [Fact]
    public void Schedule_freezes_competencies_and_sets_the_feedback_sla_from_the_end_time()
    {
        var round = Round();

        Assert.Equal("Round 2 — Functional Interview", round.Title);
        Assert.Equal(Competencies, round.Competencies);
        Assert.Equal(Start.AddMinutes(60), round.EndsAt);
        Assert.Equal(round.EndsAt.AddHours(24), round.ReminderAt);
        Assert.Equal(round.EndsAt.AddHours(48), round.OverdueAt);
        Assert.Single(round.Assessments);
        Assert.IsType<InterviewScheduled>(Assert.Single(round.DomainEvents));
    }

    [Fact]
    public void Average_excludes_na_and_rounds_to_one_decimal()
    {
        Assert.Equal(3.5m, Scoring.Average(Complete().Ratings));
        Assert.Null(Scoring.Average([new Rating("credit", null, true, string.Empty)]));
    }

    [Fact]
    public void Submit_rejects_an_incomplete_form()
    {
        var round = Round();
        var input = Complete() with { Ratings = [new Rating("credit", 4, false, string.Empty)] };

        var result = round.Submit(round.Assessments[0].Id, input, "u-p1", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AssessmentStatus.Pending, round.Assessments[0].Status);
    }

    [Fact]
    public void Submit_locks_the_form_and_completes_the_round_when_the_whole_panel_is_in()
    {
        var round = Round(new PanelMember("u-p1", "P. Rao"), new PanelMember("u-p2", "S. Iyer"));
        var first = round.Assessments[0];
        var second = round.Assessments[1];

        Assert.True(round.Submit(first.Id, Complete(), "u-p1", Now).IsSuccess);
        Assert.Equal(InterviewStatus.Scheduled, round.Status);
        Assert.True(round.SaveDraft(first.Id, Complete()).IsFailure);

        Assert.True(round.Submit(second.Id, Complete(Recommendation.StronglyRecommend), "u-p2", Now).IsSuccess);
        Assert.Equal(InterviewStatus.Completed, round.Status);
        Assert.Equal(1, first.Revision);
        Assert.Equal(3.5m, first.Average);
    }

    [Fact]
    public void Supersede_needs_a_reason_and_keeps_the_original_revision()
    {
        var round = Round();
        var id = round.Assessments[0].Id;
        round.Submit(id, Complete(), "u-p1", Now);

        Assert.True(round.Supersede(id, Complete(), "typo", "u-p1", Now).IsFailure);
        var result = round.Supersede(id, Complete(Recommendation.Reservations), "Rating for risk was entered wrongly.", "u-p1", Now.AddHours(1));

        Assert.True(result.IsSuccess);
        var assessment = round.Assessments[0];
        Assert.Equal(2, assessment.Revision);
        Assert.Equal(Recommendation.Reservations, assessment.Recommendation);
        var original = Assert.Single(assessment.History);
        Assert.Equal(1, original.Revision);
        Assert.Equal(Recommendation.Recommend, original.Recommendation);
    }

    [Fact]
    public void Reschedule_needs_a_reason_and_is_refused_once_feedback_is_in()
    {
        var round = Round(new PanelMember("u-p1", "P. Rao"), new PanelMember("u-p2", "S. Iyer"));
        var later = Start.AddDays(2);

        Assert.True(round.Reschedule(later, InterviewRules.FrdDefault.Sla, "short").IsFailure);
        Assert.True(round.Reschedule(later, InterviewRules.FrdDefault.Sla, "Panel member travelling on the day.").IsSuccess);
        Assert.Equal(later, round.ScheduledFor);
        Assert.Equal(Start, Assert.IsType<InterviewScheduled>(round.DomainEvents.Last()).RescheduledFrom);

        round.Submit(round.Assessments[0].Id, Complete(), "u-p1", Now);
        var refused = round.Reschedule(later.AddDays(1), InterviewRules.FrdDefault.Sla, "Candidate asked for another slot.");
        Assert.Equal("feedback_started", refused.Error!.Code);
    }

    [Fact]
    public void Reminder_then_overdue_fire_once_each_for_pending_interviewers()
    {
        var round = Round(new PanelMember("u-p1", "P. Rao"), new PanelMember("u-p2", "S. Iyer"));
        round.Submit(round.Assessments[0].Id, Complete(), "u-p1", Now);
        round.ClearDomainEvents();

        Assert.False(round.RaiseReminderIfDue(round.ReminderAt.AddMinutes(-1)));
        Assert.True(round.RaiseReminderIfDue(round.ReminderAt));
        Assert.False(round.RaiseReminderIfDue(round.ReminderAt.AddHours(1)));
        Assert.True(round.RaiseOverdueIfDue(round.OverdueAt));
        Assert.False(round.RaiseOverdueIfDue(round.OverdueAt.AddHours(1)));

        var overdue = Assert.IsType<FeedbackOverdue>(round.DomainEvents.Last());
        Assert.Equal(["u-p2"], overdue.PendingInterviewerIds);
    }

    [Fact]
    public void Overdue_found_first_suppresses_the_reminder()
    {
        var round = Round();

        Assert.True(round.RaiseOverdueIfDue(round.OverdueAt));
        Assert.False(round.RaiseReminderIfDue(round.OverdueAt));
    }

    [Fact]
    public void Feedback_after_the_sla_is_marked_late()
    {
        var round = Round();
        round.Submit(round.Assessments[0].Id, Complete(), "u-p1", round.OverdueAt.AddMinutes(1));

        Assert.False(Assert.IsType<FeedbackSubmitted>(round.DomainEvents.Last()).WithinSla);
    }

    [Fact]
    public void Rules_follow_the_frd_templates_and_ratification_grades()
    {
        var rules = InterviewRules.FrdDefault;

        Assert.NotNull(rules.FindRound("M3", "case-study"));
        Assert.Null(rules.FindRound("E", "case-study"));
        Assert.True(rules.NeedsRatification("KMP"));
        Assert.False(rules.NeedsRatification("M1"));
    }
}

public class SelectionDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static SelectionInput Input(bool ratify) => new("APP-1", "REQ-2026-0001", ratify ? "M3" : "E", 3.8m, 3, ratify, "frd-default");

    [Fact]
    public void A_grade_without_ratification_is_ratified_at_once()
    {
        var decision = SelectionDecision.Start(Input(false), "u-ta", Now);

        Assert.Equal(SelectionStatus.Ratified, decision.Status);
        Assert.IsType<SelectionRatified>(Assert.Single(decision.DomainEvents));
    }

    [Fact]
    public void Ratification_is_idempotent_and_a_rejection_can_be_resubmitted()
    {
        var decision = SelectionDecision.Start(Input(true), "u-ta", Now);
        Assert.Equal(SelectionStatus.PendingRatification, decision.Status);
        decision.AwaitRatification(Guid.NewGuid());

        Assert.True(decision.ApplyRatification(false, "u-hh", "Need a second functional round.", Now).IsSuccess);
        Assert.True(decision.ApplyRatification(false, "u-hh", "Need a second functional round.", Now).IsSuccess);
        Assert.Equal(SelectionStatus.Rejected, decision.Status);

        Assert.True(decision.Resubmit(Input(true), "u-ta", Now).IsSuccess);
        Assert.True(decision.ApplyRatification(true, "u-hh", null, Now).IsSuccess);
        Assert.Equal(SelectionStatus.Ratified, decision.Status);
        Assert.True(decision.Resubmit(Input(true), "u-ta", Now).IsFailure);
    }
}
