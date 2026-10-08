using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Offer.Application.Offers.Commands;
using Recuro.Offer.Domain.Offers;
using Recuro.Offer.Infrastructure.Persistence;

namespace Recuro.Offer.Infrastructure.Jobs;

public sealed class OfferLifecycleOptions
{
    public const string SectionName = "OfferLifecycle";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// RCU-OFR-006 lifecycle scanner. Each tick expires sent offers past their validity and chases the rest
/// that are due, for every tenant. One replica at a time (advisory lock, BNFR-4).
/// </summary>
public sealed partial class OfferLifecycleJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<OfferLifecycleOptions> options,
    ILogger<OfferLifecycleJob> logger) : BackgroundService
{
    private const string LockName = "offer:lifecycle";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ScanFailed(logger, ex);
            }
        }
    }

    /// <summary>One tick over every tenant. Returns false when another replica holds the lease. Public for tests.</summary>
    public async Task<bool> ScanAllTenantsAsync(CancellationToken ct)
    {
        await using var leaseScope = scopes.CreateAsyncScope();
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<OfferDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: the one place allowed to list tenants; each scan then runs inside its tenant's scope.
        var now = clock.GetUtcNow();
        var tenants = await leaseDb.Offers.IgnoreQueryFilters()
            .Where(o => o.State == OfferState.Sent && ((o.NextChaseAt != null && o.NextChaseAt <= now) || (o.ExpiresAt != null && o.ExpiresAt <= now)))
            .Select(o => o.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
            context.SetTenant(tenantId);
            context.SetUser("system:offer-lifecycle", "Offer lifecycle scanner", []);
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ScanOfferLifecycleCommand, int>>();
            var result = await handler.Handle(new ScanOfferLifecycleCommand(), ct);
            if (result.IsSuccess && result.Value > 0)
            {
                Raised(logger, tenantId, result.Value);
            }
        }

        await lease.CommitAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Offer lifecycle scan failed")]
    private static partial void ScanFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Offer lifecycle scan chased or expired {Count} offers for tenant {TenantId}")]
    private static partial void Raised(ILogger logger, Guid tenantId, int count);
}
