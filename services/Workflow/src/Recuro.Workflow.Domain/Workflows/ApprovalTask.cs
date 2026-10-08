using Recuro.BuildingBlocks.Domain;

namespace Recuro.Workflow.Domain.Workflows;

/// <summary>One approver's task on one leg (an item in the approvals inbox). Changed only through its instance.</summary>
public sealed class ApprovalTask : Entity, ITenantOwned
{
    private List<EscalationStep> _escalations = [];
    private List<ReminderStep> _reminders = [];

    private ApprovalTask()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid InstanceId { get; private set; }

    public int LegIndex { get; private set; }

    public string AssigneeRole { get; private set; } = string.Empty;

    public string AssigneeLabel { get; private set; } = string.Empty;

    public ApprovalTaskStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int? SlaWorkingDays { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>Set while a query is open: the SLA clock is stopped (RCU-WFL-004).</summary>
    public DateTimeOffset? PausedAt { get; private set; }

    public IReadOnlyList<EscalationStep> Escalations => _escalations;

    /// <summary>Highest escalation level fired so far; 0 when none.</summary>
    public int EscalationLevel { get; private set; }

    /// <summary>When the next unfired escalation is due, for the scheduler's index. Null when none is pending.</summary>
    public DateTimeOffset? NextEscalationAt { get; private set; }

    public IReadOnlyList<ReminderStep> Reminders => _reminders;

    /// <summary>When the next unfired reminder is due, for the scheduler's index. Null when none is pending.</summary>
    public DateTimeOffset? NextReminderAt { get; private set; }

    public TaskDecision? Decision { get; private set; }

    public bool IsOpen => Status == ApprovalTaskStatus.Open;

    internal static ApprovalTask Create(Guid instanceId, int legIndex, LegAssignee assignee, int? slaWorkingDays, IReadOnlyList<LegEscalation> escalation, TaskSchedule schedule, DateTimeOffset now)
    {
        var steps = escalation
            .Zip(schedule.EscalationsAt, (e, at) => (e, at))
            .Select((pair, i) => new EscalationStep(i + 1, pair.e.Role, pair.e.Label, pair.at, null))
            .ToList();
        var task = new ApprovalTask
        {
            Id = Guid.CreateVersion7(),
            InstanceId = instanceId,
            LegIndex = legIndex,
            AssigneeRole = assignee.Role,
            AssigneeLabel = assignee.Label,
            Status = ApprovalTaskStatus.Open,
            CreatedAt = now,
            SlaWorkingDays = slaWorkingDays,
            DueAt = schedule.DueAt,
            _escalations = steps,
            _reminders = RemindersFor(schedule),
        };
        task.RefreshNextEscalation();
        task.RefreshNextReminder();
        return task;
    }

    internal void Complete(TaskDecision decision)
    {
        Decision = decision;
        Status = ApprovalTaskStatus.Completed;
        PausedAt = null;
        NextEscalationAt = null;
        NextReminderAt = null;
    }

    internal void Cancel()
    {
        Status = ApprovalTaskStatus.Cancelled;
        NextEscalationAt = null;
        NextReminderAt = null;
    }

    internal bool Pause(DateTimeOffset now)
    {
        if (PausedAt is not null)
        {
            return false;
        }

        PausedAt = now;
        return true;
    }

    /// <summary>The clock restarts: the deadline and unfired escalations move by the time spent paused.</summary>
    internal void Resume(DateTimeOffset now)
    {
        var paused = now - PausedAt!.Value;
        PausedAt = null;
        DueAt += paused;
        _escalations = _escalations.Select(s => s.FiredAt is null ? s with { At = s.At + paused } : s).ToList();
        _reminders = _reminders.Select(r => r.FiredAt is null ? r with { At = r.At + paused } : r).ToList();
        RefreshNextEscalation();
        RefreshNextReminder();
    }

    /// <summary>Fires every due, unfired reminder (each fires once only), returning them in order.</summary>
    internal IReadOnlyList<ReminderStep> FireDueReminders(DateTimeOffset now)
    {
        if (!IsOpen || PausedAt is not null)
        {
            return [];
        }

        var due = _reminders.Where(r => r.FiredAt is null && r.At <= now).ToList();
        if (due.Count == 0)
        {
            return [];
        }

        _reminders = _reminders.Select(r => due.Contains(r) ? r with { FiredAt = now } : r).ToList();
        RefreshNextReminder();
        return due.Select(r => r with { FiredAt = now }).ToList();
    }

    /// <summary>Fires every due, unfired step at once (each fires once only), returning them in order.</summary>
    internal IReadOnlyList<EscalationStep> FireDue(DateTimeOffset now)
    {
        if (!IsOpen || PausedAt is not null)
        {
            return [];
        }

        var due = _escalations.Where(s => s.FiredAt is null && s.At <= now).ToList();
        if (due.Count == 0)
        {
            return [];
        }

        _escalations = _escalations.Select(s => due.Contains(s) ? s with { FiredAt = now } : s).ToList();
        EscalationLevel = due.Max(s => s.Level);
        RefreshNextEscalation();
        return due.Select(s => s with { FiredAt = now }).ToList();
    }

    private static List<ReminderStep> RemindersFor(TaskSchedule schedule)
    {
        if (schedule.DueAt is not { } due)
        {
            return [];
        }

        var reminders = new List<ReminderStep>();
        if (schedule.HalfwayAt is { } halfway && halfway < due)
        {
            reminders.Add(new ReminderStep(50, halfway, null));
        }

        reminders.Add(new ReminderStep(100, due, null));
        return reminders;
    }

    private void RefreshNextReminder() =>
        NextReminderAt = _reminders.Where(r => r.FiredAt is null).Select(r => (DateTimeOffset?)r.At).Min();

    private void RefreshNextEscalation() =>
        NextEscalationAt = _escalations.Where(s => s.FiredAt is null).Select(s => (DateTimeOffset?)s.At).Min();
}
