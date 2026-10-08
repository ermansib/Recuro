using Recuro.BuildingBlocks.Domain;

namespace Recuro.Interview.Domain.Interviews;

/// <summary>
/// One interview round for one application: when, how, who sits on the panel, which competencies the
/// Annexure-B form rates, and one assessment per panel member. The aggregate guards the feedback SLA
/// (RCU-INT-004) and the lock on submitted feedback (RCU-INT-003).
/// </summary>
public sealed class InterviewRound : AggregateRoot, ITenantOwned
{
    private readonly List<Assessment> _assessments = [];
    private List<string> _competencies = [];
    private List<string> _attachments = [];

    private InterviewRound()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    public string Grade { get; private set; } = string.Empty;

    /// <summary>Template key, e.g. <c>functional</c>.</summary>
    public string RoundType { get; private set; } = string.Empty;

    /// <summary>The template's name for the round, e.g. "Functional Interview".</summary>
    public string RoundLabel { get; private set; } = string.Empty;

    /// <summary>1 for the application's first round, and so on.</summary>
    public int RoundNumber { get; private set; }

    /// <summary>What the panel and the frontend see, e.g. "Round 2 — Functional Interview".</summary>
    public string Title => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Round {RoundNumber} — {RoundLabel}");

    public InterviewMode Mode { get; private set; }

    public DateTimeOffset ScheduledFor { get; private set; }

    public int DurationMinutes { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public InterviewStatus Status { get; private set; }

    /// <summary>Competency ids from the requisition's JD, frozen at scheduling (RCU-INT-002).</summary>
    public IReadOnlyList<string> Competencies => _competencies;

    /// <summary>Assessment material from the JD, e.g. a case-study reference (RCU-INT-001).</summary>
    public IReadOnlyList<string> Attachments => _attachments;

    /// <summary>End + the reminder threshold (24h by default): pending interviewers are reminded.</summary>
    public DateTimeOffset ReminderAt { get; private set; }

    /// <summary>End + the SLA (48h by default): feedback is overdue and escalates. The frontend's <c>feedbackDueAt</c>.</summary>
    public DateTimeOffset OverdueAt { get; private set; }

    public DateTimeOffset? ReminderSentAt { get; private set; }

    public DateTimeOffset? OverdueRaisedAt { get; private set; }

    /// <summary>Rules version the template and SLA came from.</summary>
    public string ConfigVersionId { get; private set; } = string.Empty;

    public string ScheduledBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<Assessment> Assessments => _assessments;

    public IReadOnlyList<string> PendingInterviewerIds =>
        _assessments.Where(a => !a.IsSubmitted).Select(a => a.InterviewerId).ToList();

    public static InterviewRound Schedule(RoundPlan plan, string scheduledBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var round = new InterviewRound
        {
            Id = Guid.CreateVersion7(),
            AppId = plan.AppId,
            ReqId = plan.ReqId,
            CandidateId = plan.CandidateId,
            Grade = plan.Grade,
            RoundType = plan.RoundType,
            RoundLabel = plan.RoundLabel,
            RoundNumber = plan.RoundNumber,
            Mode = plan.Mode,
            DurationMinutes = plan.DurationMinutes,
            Status = InterviewStatus.Scheduled,
            _competencies = plan.Competencies.Distinct(StringComparer.Ordinal).ToList(),
            _attachments = plan.Attachments.ToList(),
            ConfigVersionId = plan.Sla.ConfigVersionId,
            ScheduledBy = scheduledBy,
            CreatedAt = now,
        };
        round.SetTimes(plan.ScheduledFor, plan.Sla);
        round._assessments.AddRange(plan.Panel.DistinctBy(p => p.Id, StringComparer.Ordinal).Select(p => new Assessment(round.Id, p)));
        round.Raise(new InterviewScheduled(round, null));
        return round;
    }

    /// <summary>RCU-INT-007: move the round. Only before anyone has submitted feedback.</summary>
    public Result Reschedule(DateTimeOffset scheduledFor, FeedbackSla sla, string reason)
    {
        ArgumentNullException.ThrowIfNull(sla);
        var open = EnsureOpen();
        if (open.IsFailure)
        {
            return open;
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < InterviewLimits.MinReasonLength)
        {
            return InterviewErrors.ReasonRequired();
        }

        if (_assessments.Any(a => a.IsSubmitted))
        {
            return Error.Conflict("feedback_started", $"Feedback is already submitted for {Title}; schedule a new round instead.");
        }

        var previous = ScheduledFor;
        SetTimes(scheduledFor, sla);
        ReminderSentAt = null;
        OverdueRaisedAt = null;
        Raise(new InterviewScheduled(this, previous));
        return Result.Success();
    }

    public Result SaveDraft(Guid assessmentId, AssessmentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var found = Find(assessmentId);
        if (found.IsFailure)
        {
            return found;
        }

        var open = EnsureOpen();
        return open.IsFailure ? open : found.Value.SaveDraft(_competencies, input);
    }

    /// <summary>RCU-INT-003: submit and lock. The round completes when the last panel member submits.</summary>
    public Result Submit(Guid assessmentId, AssessmentInput input, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var found = Find(assessmentId);
        if (found.IsFailure)
        {
            return found;
        }

        var open = EnsureOpen();
        if (open.IsFailure)
        {
            return open;
        }

        var assessment = found.Value;
        var submitted = assessment.Submit(_competencies, input, by, now);
        if (submitted.IsFailure)
        {
            return submitted;
        }

        Raise(new FeedbackSubmitted(this, assessment, now <= OverdueAt));
        if (_assessments.All(a => a.IsSubmitted))
        {
            Status = InterviewStatus.Completed;
        }

        return Result.Success();
    }

    /// <summary>RCU-INT-003: replace submitted feedback with a new revision; the original is kept.</summary>
    public Result Supersede(Guid assessmentId, AssessmentInput input, string reason, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var found = Find(assessmentId);
        if (found.IsFailure)
        {
            return found;
        }

        if (Status == InterviewStatus.Cancelled)
        {
            return InterviewErrors.RoundClosed(Id, Status);
        }

        var superseded = found.Value.Supersede(_competencies, input, reason, by, now);
        if (superseded.IsFailure)
        {
            return superseded;
        }

        Raise(new FeedbackSubmitted(this, found.Value, now <= OverdueAt));
        return Result.Success();
    }

    /// <summary>The application left the Interview stage: nothing more is chased.</summary>
    public void Cancel(string reason)
    {
        if (Status == InterviewStatus.Scheduled)
        {
            Status = InterviewStatus.Cancelled;
            CancelReason = reason;
        }
    }

    /// <summary>RCU-INT-004 first threshold. True when a reminder was raised now (once per schedule).</summary>
    public bool RaiseReminderIfDue(DateTimeOffset now)
    {
        if (Status != InterviewStatus.Scheduled || ReminderSentAt is not null || now < ReminderAt || PendingInterviewerIds.Count == 0)
        {
            return false;
        }

        ReminderSentAt = now;
        Raise(new FeedbackReminderDue(this, PendingInterviewerIds));
        return true;
    }

    /// <summary>RCU-INT-004 SLA breach: feedback overdue, escalated per the matrix. Once per schedule.</summary>
    public bool RaiseOverdueIfDue(DateTimeOffset now)
    {
        if (Status != InterviewStatus.Scheduled || OverdueRaisedAt is not null || now < OverdueAt || PendingInterviewerIds.Count == 0)
        {
            return false;
        }

        OverdueRaisedAt = now;

        // Never remind after escalating: a scan that finds both due at once sends only the escalation.
        ReminderSentAt ??= now;
        Raise(new FeedbackOverdue(this, PendingInterviewerIds));
        return true;
    }

    public Result<Assessment> Find(Guid assessmentId)
    {
        var assessment = _assessments.FirstOrDefault(a => a.Id == assessmentId);
        return assessment is null ? InterviewErrors.AssessmentNotFound(assessmentId) : assessment;
    }

    private Result EnsureOpen() =>
        Status == InterviewStatus.Scheduled ? Result.Success() : InterviewErrors.RoundClosed(Id, Status);

    private void SetTimes(DateTimeOffset scheduledFor, FeedbackSla sla)
    {
        ScheduledFor = scheduledFor;
        EndsAt = scheduledFor.AddMinutes(DurationMinutes);
        ReminderAt = EndsAt + sla.ReminderAfter;
        OverdueAt = EndsAt + sla.OverdueAfter;
    }
}

/// <summary>Feedback SLA thresholds after the interview ends (RCU-INT-004), with the rules version they came from.</summary>
public sealed record FeedbackSla(TimeSpan ReminderAfter, TimeSpan OverdueAfter, string ConfigVersionId);

/// <summary>Everything scheduling resolved before the round is created.</summary>
public sealed record RoundPlan(
    string AppId,
    string ReqId,
    string CandidateId,
    string Grade,
    string RoundType,
    string RoundLabel,
    int RoundNumber,
    InterviewMode Mode,
    DateTimeOffset ScheduledFor,
    int DurationMinutes,
    IReadOnlyList<PanelMember> Panel,
    IReadOnlyList<string> Competencies,
    IReadOnlyList<string> Attachments,
    FeedbackSla Sla);
