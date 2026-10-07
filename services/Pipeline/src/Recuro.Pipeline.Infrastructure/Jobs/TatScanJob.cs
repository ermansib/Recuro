using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Pipeline.Application.Applications.Commands.ScanTatBreaches;
using Recuro.Pipeline.Domain.Applications;
using Recuro.Pipeline.Infrastructure.Persistence;

namespace Recuro.Pipeline.Infrastructure.Jobs;

public sealed class TatScanOptions
{
    public const string SectionName = "TatScan";

    public bool Enabled { get; set; } = true;

    /// <summary>RCU-PPL-005: every five minutes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// RCU-PPL-005 TAT scanner. Each tick checks every tenant's stage clocks and raises
/// <c>pipeline.tat.breached</c> once per stage. One replica at a time (advisory lock, BNFR-4).
/// </summary>
public sealed partial class TatScanJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<TatScanOptions> options,
    ILogger<TatScanJob> logger) : BackgroundService
{
    private const string LockName = "pipeline:tat-scan";
    private static readonly ApplicationStage[] Closed = [ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Confirmed, ApplicationStage.Hold];

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
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: the one place allowed to list tenants; each scan then runs inside its tenant's scope.
        var tenants = await leaseDb.Applications.IgnoreQueryFilters()
            .Where(a => a.TatBreachedAt == null && !Closed.Contains(a.Stage))
            .Select(a => a.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
            context.SetTenant(tenantId);
            context.SetUser("system:tat-scanner", "TAT scanner", []);
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ScanTatBreachesCommand, int>>();
            var result = await handler.Handle(new ScanTatBreachesCommand(), ct);
            if (result.IsSuccess && result.Value > 0)
            {
                Breaches(logger, tenantId, result.Value);
            }
        }

        await lease.CommitAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "TAT scan failed")]
    private static partial void ScanFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "TAT scan flagged {Count} breaches for tenant {TenantId}")]
    private static partial void Breaches(ILogger logger, Guid tenantId, int count);
}
