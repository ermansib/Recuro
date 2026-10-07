using Recuro.BuildingBlocks.Domain;

namespace Recuro.Audit.Domain.Entries;

/// <summary>
/// One immutable line of a tenant's audit trail. Entries form a hash chain per tenant: each one
/// includes the previous entry's hash, so changing or removing any entry breaks every hash after it
/// (RCU-AUD-002). There is deliberately no method that changes an entry.
/// </summary>
public sealed class AuditEntry : Entity, ITenantOwned
{
    private AuditEntry()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Position in the tenant's chain, from 1 with no gaps.</summary>
    public long Sequence { get; private set; }

    public string? ActorId { get; private set; }

    public string ActorName { get; private set; } = string.Empty;

    public string? ActorRole { get; private set; }

    public string Entity { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string? Before { get; private set; }

    public string? After { get; private set; }

    public string? Reason { get; private set; }

    public string? ConfigVersion { get; private set; }

    public string? IpAddress { get; private set; }

    public string? CorrelationId { get; private set; }

    public Guid? SourceEventId { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public string PreviousHash { get; private set; } = string.Empty;

    public string Hash { get; private set; } = string.Empty;

    /// <summary>Appends <paramref name="record"/> after <paramref name="previous"/> (null for the tenant's first entry).</summary>
    public static AuditEntry Append(Guid tenantId, AuditEntry? previous, AuditRecord record, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry needs a tenant.", nameof(tenantId));
        }

        if (previous is not null && previous.TenantId != tenantId)
        {
            throw new ArgumentException("The previous entry belongs to another tenant.", nameof(previous));
        }

        var entry = new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Sequence = (previous?.Sequence ?? 0) + 1,
            ActorId = record.ActorId,
            ActorName = record.ActorName,
            ActorRole = record.ActorRole,
            Entity = record.Entity,
            Action = record.Action,
            Before = record.Before,
            After = record.After,
            Reason = record.Reason,
            ConfigVersion = record.ConfigVersion,
            IpAddress = record.IpAddress,
            CorrelationId = record.CorrelationId,
            SourceEventId = record.SourceEventId,
            Source = record.Source,
            // PostgreSQL keeps microseconds; truncate first so the hash survives a round trip.
            OccurredAt = AuditChain.ToStoredPrecision(record.OccurredAt),
            RecordedAt = AuditChain.ToStoredPrecision(recordedAt),
            PreviousHash = previous?.Hash ?? AuditChain.GenesisHash,
        };
        entry.Hash = AuditChain.ComputeHash(entry);
        return entry;
    }
}
