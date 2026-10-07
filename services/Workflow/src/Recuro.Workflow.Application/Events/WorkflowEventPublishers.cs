using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Events;

// Domain events → integration events through the outbox (RCU-PLT-002). Payloads follow RCU-BKD-001 §5
// ("taskId, type, assignee, decision?") and services/contracts/events/workflow.*.schema.json.

internal sealed record TaskCreatedPayload(
    Guid TaskId,
    Guid InstanceId,
    string Type,
    string SubjectType,
    string SubjectId,
    string Assignee,
    DateTimeOffset? DueAt,
    string ConfigVersionId);

internal sealed record TaskCompletedPayload(
    Guid TaskId,
    Guid InstanceId,
    string Type,
    string SubjectType,
    string SubjectId,
    string Assignee,
    string Decision,
    string ActionId,
    string? Reason,
    string InstanceStatus,
    string ConfigVersionId);

internal sealed record EscalatedPayload(
    Guid TaskId,
    Guid InstanceId,
    string Type,
    string SubjectType,
    string SubjectId,
    string Assignee,
    int Level,
    string EscalateTo,
    DateTimeOffset? DueAt);

/// <summary>
/// RCU-WFL-003. Tasks are assigned to a role, so <c>assigneeIds</c> is empty and <c>assigneeRole</c> names
/// who to remind; Notification resolves the role's users through Identity.
/// </summary>
internal sealed record ReminderDuePayload(
    Guid TaskId,
    Guid InstanceId,
    string Leg,
    string SubjectType,
    string SubjectId,
    IReadOnlyList<string> AssigneeIds,
    string AssigneeRole,
    int ThresholdPercent,
    DateTimeOffset? DueAt);

internal sealed record SlaPayload(
    Guid TaskId,
    Guid InstanceId,
    string Type,
    string SubjectType,
    string SubjectId,
    string Assignee,
    DateTimeOffset? DueAt);

internal static class Subjects
{
    public static string For(ApprovalTask task) => $"WorkflowTask/{task.Id}";
}

internal sealed class PublishTaskCreated(IIntegrationEventPublisher publisher) : IDomainEventHandler<TaskCreated>
{
    public Task Handle(TaskCreated domainEvent, CancellationToken ct)
    {
        var (i, t) = (domainEvent.Instance, domainEvent.Task);
        publisher.Publish(
            EventTypes.Workflow.TaskCreated,
            Subjects.For(t),
            new TaskCreatedPayload(t.Id, i.Id, i.Type, i.SubjectType, i.SubjectId, t.AssigneeRole, t.DueAt, i.ConfigVersionId));
        return Task.CompletedTask;
    }
}

internal sealed class PublishTaskCompleted(IIntegrationEventPublisher publisher) : IDomainEventHandler<TaskCompleted>
{
    public Task Handle(TaskCompleted domainEvent, CancellationToken ct)
    {
        var (i, t) = (domainEvent.Instance, domainEvent.Task);
        var d = t.Decision!;
        publisher.Publish(
            EventTypes.Workflow.TaskCompleted,
            Subjects.For(t),
            new TaskCompletedPayload(
                t.Id,
                i.Id,
                i.Type,
                i.SubjectType,
                i.SubjectId,
                t.AssigneeRole,
                d.Effect == ActionEffect.Reject ? "reject" : "approve",
                d.ActionId,
                d.Reason,
                i.Status.ToString(),
                i.ConfigVersionId));
        return Task.CompletedTask;
    }
}

internal sealed class PublishTaskEscalated(IIntegrationEventPublisher publisher) : IDomainEventHandler<TaskEscalated>
{
    public Task Handle(TaskEscalated domainEvent, CancellationToken ct)
    {
        var (i, t, s) = (domainEvent.Instance, domainEvent.Task, domainEvent.Step);
        publisher.Publish(
            EventTypes.Workflow.Escalated,
            Subjects.For(t),
            new EscalatedPayload(t.Id, i.Id, i.Type, i.SubjectType, i.SubjectId, t.AssigneeRole, s.Level, s.Role, t.DueAt));
        return Task.CompletedTask;
    }
}

internal sealed class PublishTaskReminderDue(IIntegrationEventPublisher publisher) : IDomainEventHandler<TaskReminderDue>
{
    public Task Handle(TaskReminderDue domainEvent, CancellationToken ct)
    {
        var (i, t, r) = (domainEvent.Instance, domainEvent.Task, domainEvent.Step);
        publisher.Publish(
            EventTypes.Workflow.TaskReminderDue,
            Subjects.For(t),
            new ReminderDuePayload(t.Id, i.Id, i.Legs[t.LegIndex].Name, i.SubjectType, i.SubjectId, [], t.AssigneeRole, r.ThresholdPercent, t.DueAt));
        return Task.CompletedTask;
    }
}

internal sealed class PublishSlaPaused(IIntegrationEventPublisher publisher) : IDomainEventHandler<SlaPaused>
{
    public Task Handle(SlaPaused domainEvent, CancellationToken ct)
    {
        var (i, t) = (domainEvent.Instance, domainEvent.Task);
        publisher.Publish(EventTypes.Workflow.SlaPaused, Subjects.For(t), new SlaPayload(t.Id, i.Id, i.Type, i.SubjectType, i.SubjectId, t.AssigneeRole, t.DueAt));
        return Task.CompletedTask;
    }
}

internal sealed class PublishSlaResumed(IIntegrationEventPublisher publisher) : IDomainEventHandler<SlaResumed>
{
    public Task Handle(SlaResumed domainEvent, CancellationToken ct)
    {
        var (i, t) = (domainEvent.Instance, domainEvent.Task);
        publisher.Publish(EventTypes.Workflow.SlaResumed, Subjects.For(t), new SlaPayload(t.Id, i.Id, i.Type, i.SubjectType, i.SubjectId, t.AssigneeRole, t.DueAt));
        return Task.CompletedTask;
    }
}
