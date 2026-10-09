using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Onboarding.Application.Cases.Commands;
using Recuro.Onboarding.Domain.Cases;
using Recuro.Onboarding.Infrastructure.Persistence;

namespace Recuro.Onboarding.Infrastructure.Jobs;

public sealed class MilestoneSchedulerOptions
{
    public const string SectionName = "MilestoneScheduler";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// RCU-ONB-001/004 scheduler. Each tick raises the touchpoints and probation milestones whose date has
/// come and opens due IT/Admin tickets, for every tenant. One replica at a time (advisory lock, BNFR-4).
/// </summary>
public sealed partial class MilestoneSchedulerJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<MilestoneSchedulerOptions> options,
    ILogger<MilestoneSchedulerJob> logger) : BackgroundService
{
    private const string LockName = "onboarding:milestones";
    private static readonly CaseStatus[] Running = [CaseStatus.PreBoarding, CaseStatus.Day1Ready];

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
                await ScanAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ScanFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One tick over every tenant. Returns false when another replica holds the lease. Public for tests.</summary>
    public async Task<bool> ScanAllTenantsAsync(CancellationToken ct)
    {
        await using var leaseScope = scopes.CreateAsyncScope();
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: the one place allowed to list tenants; each scan then runs inside its tenant's scope.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var tenants = await leaseDb.Cases.IgnoreQueryFilters()
            .Where(c => Running.Contains(c.Status) && c.Milestones.Any(m => m.Status == MilestoneStatus.Scheduled && m.DueOn <= today))
            .Select(c => c.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
            context.SetTenant(tenantId);
            context.SetUser("system:onboarding-scheduler", "Onboarding scheduler", []);
            context.MakeCurrent();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ScanMilestonesCommand, int>>();
            var result = await handler.Handle(new ScanMilestonesCommand(), ct);
            if (result.IsSuccess && result.Value > 0)
            {
                Raised(logger, tenantId, result.Value);
            }
        }

        await lease.CommitAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Onboarding milestone scan failed")]
    private static partial void ScanFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Onboarding scan moved {Count} milestones for tenant {TenantId}")]
    private static partial void Raised(ILogger logger, Guid tenantId, int count);
}
