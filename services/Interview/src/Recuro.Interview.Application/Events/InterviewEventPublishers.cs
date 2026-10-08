using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Application.Events;

// Domain events → integration events, written to the outbox in the same transaction as the change
// (RCU-PLT-002). Payloads follow RCU-BKD-001 §5 and services/contracts/events/interview.*.schema.json.
// Ids only: no candidate PII leaves this service on the bus.

/// <summary><c>interview.scheduled.v1</c>: a round is booked or moved (<c>rescheduledFrom</c> set).</summary>
internal sealed record InterviewScheduledPayload(
    Guid InterviewId,
    string AppId,
    string ReqId,
    string CandidateId,
    int RoundId,
    string Round,
    string RoundType,
    InterviewMode Mode,
    IReadOnlyList<string> Panel,
    DateTimeOffset ScheduledFor,
    DateTimeOffset EndsAt,
    DateTimeOffset DueAt,
    DateTimeOffset? RescheduledFrom);

/// <summary><c>interview.feedback.submitted.v1</c>, with the computed average and SLA adherence for KPIs.</summary>
internal sealed record FeedbackSubmittedPayload(
    Guid InterviewId,
    Guid AssessmentId,
    string AppId,
    string ReqId,
    string Round,
    string InterviewerId,
    decimal? Avg,
    Recommendation? Recommendation,
    int Revision,
    bool WithinSla,
    DateTimeOffset SubmittedAt,
    bool RoundComplete);

/// <summary><c>interview.feedback.reminder_due.v1</c> (schema in contracts/events).</summary>
internal sealed record FeedbackReminderDuePayload(
    Guid InterviewId,
    string AppId,
    string ReqId,
    string Round,
    IReadOnlyList<string> PendingInterviewerIds,
    DateTimeOffset EndedAt,
    DateTimeOffset OverdueAt);

/// <summary>
/// <c>interview.feedback.overdue.v1</c>. <c>escalationIssue</c> names the Config escalation row
/// (<c>feedback-delay</c>: HOD, then HR Head); until Identity resolves the HOD, Notification sends it
/// to the panel (the interviewers still pending) and HR-TA. <c>roundId</c> and <c>dueAt</c> fill its template.
/// </summary>
internal sealed record FeedbackOverduePayload(
    Guid InterviewId,
    string AppId,
    string ReqId,
    int RoundId,
    string Round,
    IReadOnlyList<string> Panel,
    IReadOnlyList<string> PendingInterviewerIds,
    DateTimeOffset EndedAt,
    DateTimeOffset DueAt,
    DateTimeOffset OverdueAt,
    string EscalationIssue);

/// <summary><c>interview.selection.ratified.v1</c>: Pipeline moves the application from Interview to Selection.</summary>
internal sealed record SelectionRatifiedPayload(string AppId, string ReqId, string RatifiedBy, decimal? Avg, int Rounds);

internal sealed class InterviewEventPublishers(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<InterviewScheduled>,
      IDomainEventHandler<FeedbackSubmitted>,
      IDomainEventHandler<FeedbackReminderDue>,
      IDomainEventHandler<FeedbackOverdue>,
      IDomainEventHandler<SelectionRatified>
{
    public const string FeedbackDelayIssue = "feedback-delay";

    public Task Handle(InterviewScheduled domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var r = domainEvent.Round;
        publisher.Publish(
            EventTypes.Interview.Scheduled,
            Subject(r),
            new InterviewScheduledPayload(
                r.Id, r.AppId, r.ReqId, r.CandidateId, r.RoundNumber, r.Title, r.RoundType, r.Mode,
                r.Assessments.Select(a => a.InterviewerId).ToList(), r.ScheduledFor, r.EndsAt, r.OverdueAt, domainEvent.RescheduledFrom));
        return Task.CompletedTask;
    }

    public Task Handle(FeedbackSubmitted domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var r = domainEvent.Round;
        var a = domainEvent.Assessment;
        publisher.Publish(
            EventTypes.Interview.FeedbackSubmitted,
            Subject(r),
            new FeedbackSubmittedPayload(
                r.Id, a.Id, r.AppId, r.ReqId, r.Title, a.InterviewerId, a.Average, a.Recommendation, a.Revision,
                domainEvent.WithinSla, a.SubmittedAt!.Value, r.Assessments.All(x => x.IsSubmitted)));
        return Task.CompletedTask;
    }

    public Task Handle(FeedbackReminderDue domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var r = domainEvent.Round;
        publisher.Publish(
            EventTypes.Interview.FeedbackReminderDue,
            Subject(r),
            new FeedbackReminderDuePayload(r.Id, r.AppId, r.ReqId, NumberedLabel(r), domainEvent.PendingInterviewerIds, r.EndsAt, r.OverdueAt));
        return Task.CompletedTask;
    }

    public Task Handle(FeedbackOverdue domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var r = domainEvent.Round;
        publisher.Publish(
            EventTypes.Interview.FeedbackOverdue,
            Subject(r),
            new FeedbackOverduePayload(
                r.Id, r.AppId, r.ReqId, r.RoundNumber, NumberedLabel(r), domainEvent.PendingInterviewerIds, domainEvent.PendingInterviewerIds,
                r.EndsAt, r.OverdueAt, r.OverdueAt, FeedbackDelayIssue));
        return Task.CompletedTask;
    }

    public Task Handle(SelectionRatified domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        publisher.Publish(
            EventTypes.Interview.SelectionRatified,
            $"Application/{domainEvent.AppId}",
            new SelectionRatifiedPayload(domainEvent.AppId, domainEvent.ReqId, domainEvent.RatifiedBy, domainEvent.Average, domainEvent.Rounds));
        return Task.CompletedTask;
    }

    private static string Subject(InterviewRound round) => $"Interview/{round.Id}";

    /// <summary>"2 — Functional Interview": Notification's templates print "Round {round}".</summary>
    private static string NumberedLabel(InterviewRound round) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{round.RoundNumber} — {round.RoundLabel}");
}
