using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions.Events;

// Domain events → integration events, written to the outbox in the same transaction as the change
// (RCU-PLT-002). Payloads follow RCU-BKD-001 §5 and services/contracts/events/*.schema.json.

internal sealed record MrfSubmittedPayload(
    string ReqId,
    string Grade,
    string RouteRef,
    string ConfigVersionId,
    string BudgetStatus,
    string From,
    string To);

internal sealed record MrfDecidedPayload(string ReqId, string Decision, string? Reason, string ConfigVersionId, string From, string To);

internal sealed record SourcingUnlockedPayload(string ReqId, string? TargetClosure);

internal sealed record MrfCancelledPayload(string ReqId, string Reason, string From, string To);

internal static class Subjects
{
    public static string For(string reqId) => $"Requisition/{reqId}";
}

internal sealed class PublishRequisitionSubmitted(IIntegrationEventPublisher publisher) : IDomainEventHandler<RequisitionSubmitted>
{
    public Task Handle(RequisitionSubmitted domainEvent, CancellationToken ct)
    {
        publisher.Publish(
            EventTypes.Requisition.Submitted,
            Subjects.For(domainEvent.ReqId),
            new MrfSubmittedPayload(
                domainEvent.ReqId,
                domainEvent.Grade,
                domainEvent.WorkflowInstanceId.ToString(),
                domainEvent.ConfigVersionId,
                domainEvent.OutOfBudget ? "oob" : "in",
                nameof(RequisitionState.Draft),
                nameof(RequisitionState.PendingApproval)));
        return Task.CompletedTask;
    }
}

internal sealed class PublishRequisitionApproved(IIntegrationEventPublisher publisher) : IDomainEventHandler<RequisitionApproved>
{
    public Task Handle(RequisitionApproved domainEvent, CancellationToken ct)
    {
        var subject = Subjects.For(domainEvent.ReqId);
        publisher.Publish(
            EventTypes.Requisition.Approved,
            subject,
            new MrfDecidedPayload(domainEvent.ReqId, "approve", null, domainEvent.ConfigVersionId, nameof(RequisitionState.PendingApproval), nameof(RequisitionState.Approved)));
        publisher.Publish(
            EventTypes.Requisition.SourcingUnlocked,
            subject,
            new SourcingUnlockedPayload(domainEvent.ReqId, domainEvent.TargetClosure is null ? null : RequisitionDto.IsoDate(domainEvent.TargetClosure)));
        return Task.CompletedTask;
    }
}

internal sealed class PublishRequisitionRejected(IIntegrationEventPublisher publisher) : IDomainEventHandler<RequisitionRejected>
{
    public Task Handle(RequisitionRejected domainEvent, CancellationToken ct)
    {
        publisher.Publish(
            EventTypes.Requisition.Rejected,
            Subjects.For(domainEvent.ReqId),
            new MrfDecidedPayload(domainEvent.ReqId, "reject", domainEvent.Reason, domainEvent.ConfigVersionId, nameof(RequisitionState.PendingApproval), nameof(RequisitionState.Rejected)));
        return Task.CompletedTask;
    }
}

internal sealed class PublishRequisitionCancelled(IIntegrationEventPublisher publisher) : IDomainEventHandler<RequisitionCancelled>
{
    public Task Handle(RequisitionCancelled domainEvent, CancellationToken ct)
    {
        publisher.Publish(
            EventTypes.Requisition.Cancelled,
            Subjects.For(domainEvent.ReqId),
            new MrfCancelledPayload(domainEvent.ReqId, domainEvent.Reason, domainEvent.From.ToString(), nameof(RequisitionState.Cancelled)));
        return Task.CompletedTask;
    }
}
