using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Events;

/// <summary>Payload of <c>config.version.activated.v1</c> (RCU-BKD-001 §5: matrixType, versionId, effectiveFrom).</summary>
public sealed record ConfigVersionActivatedPayload(string MatrixType, Guid VersionId, int Number, DateTimeOffset EffectiveFrom);

/// <summary>Publishes the activation through the outbox, in the approval's transaction.</summary>
internal sealed class RuleSetActivatedPublisher(IIntegrationEventPublisher events) : IDomainEventHandler<RuleSetActivatedDomainEvent>
{
    public Task Handle(RuleSetActivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var type = domainEvent.MatrixType.ToKey();
        events.Publish(
            EventTypes.Config.VersionActivated,
            $"RuleSet/{type}/{domainEvent.VersionId}",
            new ConfigVersionActivatedPayload(type, domainEvent.VersionId, domainEvent.Number, domainEvent.EffectiveFrom));
        return Task.CompletedTask;
    }
}
