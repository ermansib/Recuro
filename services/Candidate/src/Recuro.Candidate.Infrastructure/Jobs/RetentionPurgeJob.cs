using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Candidate.Application.Candidates.Commands.PurgeCandidates;
using Recuro.Candidate.Domain.Candidates;
using Recuro.Candidate.Infrastructure.Persistence;

namespace Recuro.Candidate.Infrastructure.Jobs;

public sealed class RetentionPurgeOptions
{
    public const string SectionName = "RetentionPurge";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Report what would be purged without changing anything (RCU-CND-003 dry-run).</summary>
    public bool DryRun { get; set; }
}

/// <summary>
/// RCU-CND-003 nightly purge: anonymises every tenant's unsuccessful candidates past retention, skipping
/// legal holds. One replica at a time (advisory lock), so nothing is purged twice.
/// </summary>
public sealed partial class RetentionPurgeJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<RetentionPurgeOptions> options,
    ILogger<RetentionPurgeJob> logger) : BackgroundService
{
    private const string LockName = "candidate:retention-purge";

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
                await PurgeAllTenantsAsync(options.Value.DryRun, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run over every tenant. Returns false when another replica holds the lease. Public for tests.</summary>
    public async Task<bool> PurgeAllTenantsAsync(bool dryRun, CancellationToken ct)
    {
        await using var leaseScope = scopes.CreateAsyncScope();
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<CandidateDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: the one place allowed to list tenants; each purge then runs inside its tenant's scope.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var tenants = await leaseDb.Candidates.IgnoreQueryFilters()
            .Where(c => c.PurgedAt == null && c.RetentionStatus == RetentionStatus.Unsuccessful && c.RetainUntil < today)
            .Select(c => c.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenantId);
            scope.ServiceProvider.GetRequiredService<ScopeContext>().SetUser("system:retention-purge", "Retention purge", []);
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PurgeCandidatesCommand, PurgeReportDto>>();
            var result = await handler.Handle(new PurgeCandidatesCommand(dryRun, null), ct);
            if (result.IsSuccess)
            {
                Purged(logger, tenantId, result.Value.Count, dryRun);
            }
        }

        await lease.CommitAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Retention purge run failed")]
    private static partial void RunFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention purge for tenant {TenantId}: {Count} candidates (dry run: {DryRun})")]
    private static partial void Purged(ILogger logger, Guid tenantId, int count, bool dryRun);
}
