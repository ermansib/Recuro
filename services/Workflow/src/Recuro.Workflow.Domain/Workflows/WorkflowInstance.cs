using Recuro.BuildingBlocks.Domain;

namespace Recuro.Workflow.Domain.Workflows;

/// <summary>
/// A generic approval workflow (RCU-WFL-001): sequential legs of parallel tasks, opened by a domain
/// service for one subject (an MRF, an offer, a BGV case). The instance owns its tasks and every rule
/// about deciding them.
/// </summary>
public sealed class WorkflowInstance : AggregateRoot, ITenantOwned
{
    private List<LegDefinition> _legs = [];
    private readonly List<ApprovalTask> _tasks = [];

    private WorkflowInstance()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Workflow kind, e.g. <c>MRF</c>, <c>Offer</c>.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Entity type the decision is about, e.g. <c>Requisition</c>.</summary>
    public string SubjectType { get; private set; } = string.Empty;

    /// <summary>Entity id the decision is about, e.g. <c>REQ-2026-0156</c>.</summary>
    public string SubjectId { get; private set; } = string.Empty;

    /// <summary>The caller's idempotency key: one live instance per key.</summary>
    public string CorrelationKey { get; private set; } = string.Empty;

    /// <summary>Rules version the route was resolved under (RCU-CFG-003).</summary>
    public string ConfigVersionId { get; private set; } = string.Empty;

    public WorkflowStatus Status { get; private set; }

    public int CurrentLeg { get; private set; }

    public IReadOnlyList<LegDefinition> Legs => _legs;

    public Presentation Presentation { get; private set; } = null!;

    public string InitiatorId { get; private set; } = string.Empty;

    public string InitiatorName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Touched by every change to the instance or one of its tasks, so the row version catches two
    /// approvers finishing the same leg at once.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyList<ApprovalTask> Tasks => _tasks;

    /// <summary>Optimistic concurrency token (PostgreSQL xmin): two approvers can't both finish the same leg.</summary>
    public uint Version { get; private set; }

    public static Result<WorkflowInstance> Start(
        string type,
        string subjectType,
        string subjectId,
        string correlationKey,
        string configVersionId,
        IReadOnlyList<LegDefinition> legs,
        Presentation presentation,
        Actor initiator,
        TaskSchedule firstLegSchedule,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentNullException.ThrowIfNull(initiator);
        if (legs.Count == 0 || legs.Any(l => l.Assignees.Count == 0))
        {
            return WorkflowErrors.EmptyRoute;
        }

        var instance = new WorkflowInstance
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            SubjectType = subjectType,
            SubjectId = subjectId,
            CorrelationKey = correlationKey,
            ConfigVersionId = configVersionId,
            Status = WorkflowStatus.Active,
            _legs = legs.ToList(),
            Presentation = presentation,
            InitiatorId = initiator.Id,
            InitiatorName = initiator.Name,
            CreatedAt = now,
            UpdatedAt = now,
        };
        instance.OpenLeg(0, firstLegSchedule, now);
        return instance;
    }

    /// <summary>The leg that follows the current one, whose deadlines the caller computes before a decision.</summary>
    public LegDefinition? NextLeg => CurrentLeg + 1 < _legs.Count ? _legs[CurrentLeg + 1] : null;

    public ApprovalTask? FindTask(Guid taskId) => _tasks.Find(t => t.Id == taskId);

    /// <summary>
    /// RCU-WFL-002: the assignee decides. Decisions are final (409 on a second one); a rejection needs a
    /// reason; a query pauses the SLA instead of deciding. The last approval of a leg opens the next leg
    /// (<paramref name="nextLegSchedule"/>) or approves the instance; any rejection rejects it.
    /// </summary>
    public Result Decide(Guid taskId, Actor actor, string actionId, string? reason, TaskSchedule nextLegSchedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(nextLegSchedule);
        var task = FindTask(taskId);
        if (task is null)
        {
            return WorkflowErrors.TaskNotFound(taskId);
        }

        if (!actor.Roles.Contains(task.AssigneeRole, StringComparer.Ordinal))
        {
            return WorkflowErrors.NotAssignee;
        }

        if (!task.IsOpen)
        {
            return task.Decision is null ? WorkflowErrors.NotActive(Status) : WorkflowErrors.AlreadyDecided;
        }

        if (Status != WorkflowStatus.Active)
        {
            return WorkflowErrors.NotActive(Status);
        }

        var action = Presentation.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action is null)
        {
            return WorkflowErrors.UnknownAction(actionId);
        }

        var trimmed = reason?.Trim();
        UpdatedAt = now;
        if (action.Effect == ActionEffect.Query)
        {
            if (task.Pause(now))
            {
                Raise(new SlaPaused(this, task));
            }

            return Result.Success();
        }

        if (action.Effect == ActionEffect.Reject && (trimmed is null || trimmed.Length < WorkflowErrors.MinReasonLength))
        {
            return WorkflowErrors.ReasonRequired;
        }

        var text = action.Effect == ActionEffect.Reject ? $"{action.Label} — reason recorded" : action.ResultText ?? action.Label;
        task.Complete(new TaskDecision(action.Id, action.Effect, text, actor.Id, actor.Name, now, string.IsNullOrEmpty(trimmed) ? null : trimmed));

        if (action.Effect == ActionEffect.Reject)
        {
            Finish(WorkflowStatus.Rejected, now);
        }
        else if (_tasks.Where(t => t.LegIndex == CurrentLeg).All(t => t.Status == ApprovalTaskStatus.Completed))
        {
            if (NextLeg is null)
            {
                Finish(WorkflowStatus.Approved, now);
            }
            else
            {
                OpenLeg(CurrentLeg + 1, nextLegSchedule, now);
            }
        }

        // Raised after the instance moved on, so consumers see the final instanceStatus.
        Raise(new TaskCompleted(this, task));
        return Result.Success();
    }

    /// <summary>RCU-WFL-004: the initiator answered the query; the SLA clock restarts.</summary>
    public Result Resume(Guid taskId, DateTimeOffset now)
    {
        var task = FindTask(taskId);
        if (task is null)
        {
            return WorkflowErrors.TaskNotFound(taskId);
        }

        if (!task.IsOpen || task.PausedAt is null)
        {
            return WorkflowErrors.NotPaused;
        }

        task.Resume(now);
        UpdatedAt = now;
        Raise(new SlaResumed(this, task));
        return Result.Success();
    }

    /// <summary>The owning service withdrew the request (saga compensation or a cancelled subject).</summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status == WorkflowStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status != WorkflowStatus.Active)
        {
            return WorkflowErrors.NotActive(Status);
        }

        CancelReason = reason;
        Finish(WorkflowStatus.Cancelled, now);
        return Result.Success();
    }

    /// <summary>
    /// RCU-WFL-003/006: fires due reminders (50% and 100% of the SLA) and escalation steps on open,
    /// unpaused tasks; each fires once. Returns how many fired.
    /// </summary>
    public int FireDueEscalations(DateTimeOffset now)
    {
        if (Status != WorkflowStatus.Active)
        {
            return 0;
        }

        var fired = 0;
        foreach (var task in _tasks.Where(t => t.IsOpen))
        {
            foreach (var reminder in task.FireDueReminders(now))
            {
                Raise(new TaskReminderDue(this, task, reminder));
                fired++;
            }

            foreach (var step in task.FireDue(now))
            {
                Raise(new TaskEscalated(this, task, step));
                fired++;
            }
        }

        if (fired > 0)
        {
            UpdatedAt = now;
        }

        return fired;
    }

    private void OpenLeg(int legIndex, TaskSchedule schedule, DateTimeOffset now)
    {
        CurrentLeg = legIndex;
        var leg = _legs[legIndex];
        foreach (var assignee in leg.Assignees)
        {
            var task = ApprovalTask.Create(Id, legIndex, assignee, leg.SlaWorkingDays, leg.Escalation, schedule, now);
            _tasks.Add(task);
            Raise(new TaskCreated(this, task));
        }
    }

    private void Finish(WorkflowStatus status, DateTimeOffset now)
    {
        Status = status;
        CompletedAt = now;
        UpdatedAt = now;
        foreach (var open in _tasks.Where(t => t.IsOpen))
        {
            open.Cancel();
        }
    }
}
