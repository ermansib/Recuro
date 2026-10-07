using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Events;

/// <summary><c>candidate.created.v1</c>: ids and source only, never personal data.</summary>
public sealed record CandidateCreatedPayload(string CandidateId, string Source);

/// <summary><c>candidate.purged.v1</c>.</summary>
public sealed record CandidatePurgedPayload(string CandidateId, string PurgeScope);

/// <summary>Turns domain events into integration events through the outbox, in the same transaction.</summary>
internal sealed class PublishCandidateEvents(IIntegrationEventPublisher publisher)
    : IDomainEventHandler<CandidateCreatedDomainEvent>, IDomainEventHandler<CandidatePurgedDomainEvent>
{
    /// <summary>Personal data scrubbed, non-personal shell kept for statistics (BQ-06 default).</summary>
    public const string AnonymisedScope = "anonymised";

    public Task Handle(CandidateCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        publisher.Publish(
            EventTypes.Candidate.Created,
            Subject(domainEvent.CandidateId),
            new CandidateCreatedPayload(domainEvent.CandidateId.ToString(), CandidateSourceNames.ToName(domainEvent.Source)));
        return Task.CompletedTask;
    }

    public Task Handle(CandidatePurgedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        publisher.Publish(
            EventTypes.Candidate.Purged,
            Subject(domainEvent.CandidateId),
            new CandidatePurgedPayload(domainEvent.CandidateId.ToString(), AnonymisedScope));
        return Task.CompletedTask;
    }

    private static string Subject(Guid id) => $"Candidate/{id}";
}
