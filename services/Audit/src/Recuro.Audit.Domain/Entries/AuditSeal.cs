using Recuro.BuildingBlocks.Domain;

namespace Recuro.Audit.Domain.Entries;

/// <summary>
/// Daily checkpoint of a tenant's chain (RCU-AUD-002): the chain up to <see cref="UpToSequence"/> was
/// verified and ended in <see cref="Hash"/>. Later verification starts from the last seal.
/// </summary>
public sealed class AuditSeal : Entity, ITenantOwned
{
    private AuditSeal()
    {
    }

    public Guid TenantId { get; private set; }

    public long UpToSequence { get; private set; }

    public string Hash { get; private set; } = string.Empty;

    public DateTimeOffset SealedAt { get; private set; }

    public static AuditSeal Create(Guid tenantId, long upToSequence, string hash, DateTimeOffset sealedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        UpToSequence = upToSequence,
        Hash = hash,
        SealedAt = AuditChain.ToStoredPrecision(sealedAt),
    };
}
