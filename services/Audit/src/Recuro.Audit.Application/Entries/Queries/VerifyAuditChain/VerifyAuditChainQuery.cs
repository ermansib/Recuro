using Recuro.Audit.Application.Abstractions;
using Recuro.Audit.Domain.Entries;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Audit.Application.Entries.Queries.VerifyAuditChain;

/// <summary>Re-computes the tenant's chain from the last seal and reports the first broken link (RCU-AUD-002).</summary>
public sealed record VerifyAuditChainQuery : IQuery<ChainVerificationDto>;

public sealed record ChainVerificationDto(bool Valid, long CheckedEntries, long? BrokenAtSequence, long VerifiedFromSequence);

internal sealed class VerifyAuditChainQueryHandler(IAuditReadStore store) : IQueryHandler<VerifyAuditChainQuery, ChainVerificationDto>
{
    public async Task<Result<ChainVerificationDto>> Handle(VerifyAuditChainQuery query, CancellationToken ct)
    {
        var seal = await store.GetLastSealAsync(ct);
        var fromSequence = seal?.UpToSequence ?? 0;
        var entries = new List<AuditEntry>();
        await foreach (var entry in store.StreamChainAsync(fromSequence, ct))
        {
            entries.Add(entry);
        }

        var result = AuditChain.Verify(entries, fromSequence, seal?.Hash ?? AuditChain.GenesisHash);
        return new ChainVerificationDto(result.IsValid, result.CheckedEntries, result.BrokenAtSequence, fromSequence);
    }
}
