using Recuro.BuildingBlocks.Application.Messaging;

namespace Recuro.Audit.Application.Entries.Commands.RecordAuditEntry;

/// <summary>
/// RCU-AUD-001 direct ingest: a service records a decision or state change. When it acts on behalf of
/// a person it names them in <see cref="ActorId"/>/<see cref="ActorName"/>/<see cref="ActorRole"/>;
/// otherwise the calling account is the actor.
/// </summary>
public sealed record RecordAuditEntryCommand(
    string Entity,
    string Action,
    string? Before,
    string? After,
    string? Reason,
    string? ConfigVersion,
    string? ActorId,
    string? ActorName,
    string? ActorRole,
    DateTimeOffset? OccurredAt,
    string? IpAddress) : ICommand<AuditEventDto>;
