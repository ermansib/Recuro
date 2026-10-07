namespace Recuro.BuildingBlocks.Application.IntegrationEvents;

/// <summary>
/// Envelope fields of a received event (CloudEvents 1.0 plus Recuro extensions). The producer fills
/// them from the scope it published in.
/// </summary>
/// <param name="Id">CloudEvents <c>id</c>. Consumers dedupe on it.</param>
/// <param name="Type">CloudEvents <c>type</c>, e.g. <c>recruitment.mrf.submitted.v1</c>. See <see cref="EventTypes"/>.</param>
/// <param name="Source">CloudEvents <c>source</c>: the producing service, e.g. <c>/services/requisition</c>.</param>
/// <param name="Subject">CloudEvents <c>subject</c>: the entity, e.g. <c>Requisition/REQ-2026-0156</c>.</param>
/// <param name="Time">When it happened (UTC).</param>
/// <param name="TenantId">Extension <c>tenantid</c>. Every event belongs to exactly one tenant.</param>
/// <param name="CorrelationId">Extension <c>correlationid</c>.</param>
/// <param name="ActorId">Extension <c>actorid</c>: the user or service that caused it.</param>
/// <param name="ActorName">Extension <c>actorname</c>.</param>
/// <param name="ActorRole">Extension <c>actorrole</c>.</param>
public sealed record EventMetadata(
    Guid Id,
    string Type,
    string Source,
    string Subject,
    DateTimeOffset Time,
    Guid TenantId,
    string? CorrelationId,
    string? ActorId,
    string? ActorName,
    string? ActorRole);

/// <summary>An integration event as a consumer sees it.</summary>
public sealed record IntegrationEvent<TData>(EventMetadata Metadata, TData Data);

/// <summary>
/// Publishes integration events through the transactional outbox: the event is written to the
/// service's own database in the same transaction as the change, and a relay sends it to the broker.
/// Nothing is sent if the transaction rolls back (RCU-PLT-002).
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Queues the event. It is persisted by the next <c>SaveChangesAsync</c> of the unit of work.</summary>
    void Publish<TData>(string type, string subject, TData data)
        where TData : notnull;
}

/// <summary>
/// Handles one event type. Runs once per event id (inbox dedupe) in a transaction with the inbox row.
/// Throw to retry; after the retries the message goes to the service's dead-letter queue.
/// </summary>
public interface IIntegrationEventHandler<TData>
{
    Task Handle(IntegrationEvent<TData> integrationEvent, CancellationToken ct);
}
