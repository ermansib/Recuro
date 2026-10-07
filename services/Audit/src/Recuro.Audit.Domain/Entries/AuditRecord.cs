namespace Recuro.Audit.Domain.Entries;

/// <summary>What happened, as reported by a service or mirrored from an event (RCU-AUD-001).</summary>
/// <param name="ActorId">Subject id of the person or service account.</param>
/// <param name="ActorName">Display name at the time of the action.</param>
/// <param name="ActorRole">Role key at the time of the action, e.g. <c>hrta</c>.</param>
/// <param name="Entity">What was acted on, e.g. <c>Requisition/REQ-2026-0156</c>.</param>
/// <param name="Action">What was done, e.g. <c>STATE_CHANGE</c> or an event type.</param>
/// <param name="Before">State before, when it applies.</param>
/// <param name="After">State after, when it applies.</param>
/// <param name="Reason">Why, when the action required one.</param>
/// <param name="ConfigVersion">Rules version the decision was made under (RCU-CFG-003).</param>
/// <param name="IpAddress">Caller's address for direct ingest.</param>
/// <param name="CorrelationId">Ties the entry to the request or saga that caused it.</param>
/// <param name="SourceEventId">CloudEvents id when mirrored from the bus.</param>
/// <param name="Source">Producing service, e.g. <c>/services/requisition</c>.</param>
/// <param name="OccurredAt">When it happened (UTC).</param>
public sealed record AuditRecord(
    string? ActorId,
    string ActorName,
    string? ActorRole,
    string Entity,
    string Action,
    string? Before,
    string? After,
    string? Reason,
    string? ConfigVersion,
    string? IpAddress,
    string? CorrelationId,
    Guid? SourceEventId,
    string Source,
    DateTimeOffset OccurredAt);
