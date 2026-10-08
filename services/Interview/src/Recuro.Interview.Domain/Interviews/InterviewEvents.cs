using Recuro.BuildingBlocks.Domain;

namespace Recuro.Interview.Domain.Interviews;

/// <summary>A round was put on the calendar, or moved (<see cref="RescheduledFrom"/> set).</summary>
public sealed record InterviewScheduled(InterviewRound Round, DateTimeOffset? RescheduledFrom) : IDomainEvent;

/// <summary>A panel member submitted feedback, or superseded it with a new revision.</summary>
public sealed record FeedbackSubmitted(InterviewRound Round, Assessment Assessment, bool WithinSla) : IDomainEvent;

/// <summary>24h after the round: feedback is still missing from these interviewers.</summary>
public sealed record FeedbackReminderDue(InterviewRound Round, IReadOnlyList<string> PendingInterviewerIds) : IDomainEvent;

/// <summary>48h after the round: the SLA is breached for these interviewers.</summary>
public sealed record FeedbackOverdue(InterviewRound Round, IReadOnlyList<string> PendingInterviewerIds) : IDomainEvent;
