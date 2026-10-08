using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Reporting.Application.Reports.Commands;
using Recuro.Reporting.Infrastructure.Persistence;

namespace Recuro.Reporting.Infrastructure.Jobs;

public sealed class ReportScheduleOptions
{
    public const string SectionName = "ReportSchedule";

    public bool Enabled { get; set; } = true;

    /// <summary>How often closed periods are checked. Packs go out once per period, on the first tick after it closes.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// RCU-RPT-003 scheduler: monthly packs to HR Head and quarterly packs to MD/CEO, in each tenant's time
/// zone. Each tick checks every tenant that has reporting data; a pack is built once per period and
/// recipient. One replica at a time (advisory lock, BNFR-4).
/// </summary>
public sealed partial class ReportScheduleJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<ReportScheduleOptions> options,
    ILogger<ReportScheduleJob> logger) : BackgroundService
{
    private const string LockName = "reporting:schedule";

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
                await RunAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One tick over every tenant. Returns false when another replica holds the lease. Public for tests.</summary>
    public async Task<bool> RunAllTenantsAsync(CancellationToken ct)
    {
        await using var leaseScope = scopes.CreateAsyncScope();
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: the one place allowed to list tenants; each run then works inside its tenant's scope.
        var tenants = await leaseDb.Events.IgnoreQueryFilters().Select(e => e.TenantId)
            .Union(leaseDb.Settings.IgnoreQueryFilters().Select(s => s.TenantId))
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            try
            {
                await RunTenantAsync(tenantId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TenantFailed(logger, tenantId, ex);
            }
        }

        await lease.CommitAsync(ct);
        return true;
    }

    private async Task RunTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
        context.SetTenant(tenantId);
        context.SetUser("system:report-schedule", "Report scheduler", []);
        context.MakeCurrent();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunScheduledPacksCommand, int>>();
        var result = await handler.Handle(new RunScheduledPacksCommand(), ct);
        if (result.IsSuccess && result.Value > 0)
        {
            Sent(logger, tenantId, result.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Report schedule run failed")]
    private static partial void RunFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Report schedule failed for tenant {TenantId}")]
    private static partial void TenantFailed(ILogger logger, Guid tenantId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report schedule queued {Count} packs for tenant {TenantId}")]
    private static partial void Sent(ILogger logger, Guid tenantId, int count);
}
