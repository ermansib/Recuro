using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Application.Applications.Events;

// Payloads carry ids, never personal data (services/contracts/events/*.schema.json).

/// <summary><c>pipeline.application.created.v1</c>.</summary>
public sealed record ApplicationCreatedPayload(string AppId, string ReqId, string CandidateId, string Source);

/// <summary><c>pipeline.stage.changed.v1</c> (RCU-PPL-007: requisition, source and dwell time for funnel metrics).</summary>
public sealed record StageChangedPayload(
    string AppId,
    string ReqId,
    string CandidateId,
    string Source,
    string From,
    string To,
    string Actor,
    DateTimeOffset At,
    long DwellMs);

/// <summary><c>pipeline.application.final_rejected.v1</c>. Notification schedules the regret email for <c>regretSendAt</c>.</summary>
public sealed record FinalRejectedPayload(
    string AppId,
    string ReqId,
    string CandidateId,
    string Reason,
    DateOnly RegretSendAt,
    DateOnly RetainUntil);

/// <summary><c>pipeline.tat.breached.v1</c>.</summary>
public sealed record TatBreachedPayload(
    string Entity,
    string AppId,
    string ReqId,
    string Stage,
    DateTimeOffset DueAt,
    long VarianceMs,
    string? EscalationPath);

/// <summary>Turns domain events into integration events through the outbox, in the same transaction as the change.</summary>
internal sealed class PublishPipelineEvents(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<ApplicationCreatedDomainEvent>,
      IDomainEventHandler<StageChangedDomainEvent>,
      IDomainEventHandler<FinalRejectedDomainEvent>,
      IDomainEventHandler<TatBreachedDomainEvent>
{
    public Task Handle(ApplicationCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var a = domainEvent.Application;
        publisher.Publish(EventTypes.Pipeline.ApplicationCreated, Subject(a), new ApplicationCreatedPayload(a.AppId, a.ReqId, a.CandidateId, a.Source));
        return Task.CompletedTask;
    }

    public Task Handle(StageChangedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var a = domainEvent.Application;
        publisher.Publish(
            EventTypes.Pipeline.StageChanged,
            Subject(a),
            new StageChangedPayload(
                a.AppId,
                a.ReqId,
                a.CandidateId,
                a.Source,
                domainEvent.From.ToString(),
                domainEvent.To.ToString(),
                domainEvent.Actor.Id ?? domainEvent.Actor.Name,
                domainEvent.At,
                (long)domainEvent.Dwell.TotalMilliseconds));
        return Task.CompletedTask;
    }

    public Task Handle(FinalRejectedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var a = domainEvent.Application;
        publisher.Publish(
            EventTypes.Pipeline.FinalRejected,
            Subject(a),
            new FinalRejectedPayload(a.AppId, a.ReqId, a.CandidateId, domainEvent.Reason, domainEvent.RegretDueBy, domainEvent.RetainUntil));
        return Task.CompletedTask;
    }

    public Task Handle(TatBreachedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var a = domainEvent.Application;
        publisher.Publish(
            EventTypes.Pipeline.TatBreached,
            Subject(a),
            new TatBreachedPayload(
                Subject(a),
                a.AppId,
                a.ReqId,
                domainEvent.Stage.ToString(),
                domainEvent.DueAt,
                (long)domainEvent.Variance.TotalMilliseconds,
                string.IsNullOrEmpty(domainEvent.Escalation) ? null : domainEvent.Escalation));
        return Task.CompletedTask;
    }

    private static string Subject(Domain.Applications.Application application) => $"Application/{application.AppId}";
}
