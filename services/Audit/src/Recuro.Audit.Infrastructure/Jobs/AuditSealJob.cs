using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.Audit.Domain.Entries;
using Recuro.Audit.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Locking;

namespace Recuro.Audit.Infrastructure.Jobs;

public sealed class AuditSealOptions
{
    public const string SectionName = "AuditSeal";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>
/// Daily chain-seal checkpoint (RCU-AUD-002). Verifies each tenant's chain since its last seal and,
/// when intact, records a new seal. A broken chain is logged as critical and never sealed over.
/// Runs on one replica at a time (advisory lock), so seals are never written twice.
/// </summary>
public sealed partial class AuditSealJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<AuditSealOptions> options,
    ILogger<AuditSealJob> logger) : BackgroundService
{
    private const string LockName = "audit:seal-job";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        do
        {
            try
            {
                await SealAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run over every tenant. Returns false when another replica holds the lease. Public for tests.</summary>
    public async Task<bool> SealAllTenantsAsync(CancellationToken ct)
    {
        await using var leaseScope = scopes.CreateAsyncScope();
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: it is the one place allowed to list tenants, then it works inside each tenant's scope.
        var tenants = await leaseDb.Entries.IgnoreQueryFilters().Select(e => e.TenantId).Distinct().ToListAsync(ct);
        foreach (var tenantId in tenants)
        {
            await SealTenantAsync(tenantId, ct);
        }

        await lease.CommitAsync(ct);
        return true;
    }

    private async Task SealTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var seal = await db.Seals.AsNoTracking().OrderByDescending(s => s.UpToSequence).FirstOrDefaultAsync(ct);
        var fromSequence = seal?.UpToSequence ?? 0;
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.Sequence > fromSequence)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
        if (entries.Count == 0)
        {
            return;
        }

        var verification = AuditChain.Verify(entries, fromSequence, seal?.Hash ?? AuditChain.GenesisHash);
        if (!verification.IsValid)
        {
            ChainBroken(logger, tenantId, verification.BrokenAtSequence);
            return;
        }

        db.Seals.Add(AuditSeal.Create(tenantId, entries[^1].Sequence, verification.LastHash, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit seal run failed")]
    private static partial void RunFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Audit chain for tenant {TenantId} is broken at sequence {Sequence}; not sealed")]
    private static partial void ChainBroken(ILogger logger, Guid tenantId, long? sequence);
}
