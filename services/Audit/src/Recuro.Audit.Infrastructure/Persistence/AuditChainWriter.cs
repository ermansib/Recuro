using Microsoft.EntityFrameworkCore;
using Recuro.Audit.Application.Abstractions;
using Recuro.Audit.Domain.Entries;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Locking;

namespace Recuro.Audit.Infrastructure.Persistence;

/// <summary>
/// Appends under a per-tenant advisory lock, so concurrent writers (API replicas, the event consumer)
/// take turns and the chain never forks. Joins the caller's transaction when there is one (the event
/// consumer runs each handler in a transaction with its inbox row).
/// </summary>
internal sealed class AuditChainWriter(AuditDbContext db, ITenantContext tenant, TimeProvider clock) : IAuditChainWriter
{
    public async Task<AuditEntry> AppendAsync(AuditRecord record, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        if (db.Database.CurrentTransaction is not null)
        {
            return await AppendLockedAsync(tenantId, record, ct);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var entry = await AppendLockedAsync(tenantId, record, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return entry;
        });
    }

    private async Task<AuditEntry> AppendLockedAsync(Guid tenantId, AuditRecord record, CancellationToken ct)
    {
        await PostgresLocks.LockAsync(db, $"audit-chain:{tenantId}", ct);
        var last = await db.Entries.AsNoTracking().OrderByDescending(e => e.Sequence).FirstOrDefaultAsync(ct);
        var entry = AuditEntry.Append(tenantId, last, record, clock.GetUtcNow());
        db.Entries.Add(entry);
        return entry;
    }
}
