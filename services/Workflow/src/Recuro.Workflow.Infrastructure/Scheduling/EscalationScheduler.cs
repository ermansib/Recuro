using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Workflow.Domain.Workflows;
using Recuro.Workflow.Infrastructure.Persistence;

namespace Recuro.Workflow.Infrastructure.Scheduling;

/// <summary>
/// RCU-WFL-003/006: fires escalation steps whose time has come. Deadlines were computed on the tenant's
/// calendar when each task opened, so a run needs no other service. One replica runs at a time (an
/// advisory lock, BNFR-4); each instance is updated in its own tenant scope, and each step fires once.
/// </summary>
public sealed partial class EscalationScheduler(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<EscalationOptions> options,
    ILogger<EscalationScheduler> logger) : BackgroundService
{
    public const string LockName = "workflow:escalations";

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
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass. Returns how many escalation steps fired, or -1 when another replica holds the lock. Public for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var lockScope = scopes.CreateAsyncScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await using var transaction = await lockDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(lockDb, LockName, ct))
        {
            return -1;
        }

        var now = clock.GetUtcNow();
        var due = await lockDb.Tasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.NextEscalationAt != null && t.NextEscalationAt <= now && t.Status == ApprovalTaskStatus.Open && t.PausedAt == null)
            .Select(t => new { t.TenantId, t.InstanceId })
            .Distinct()
            .Take(options.Value.BatchSize)
            .ToListAsync(ct);

        var fired = 0;
        foreach (var item in due)
        {
            try
            {
                fired += await EscalateAsync(item.TenantId, item.InstanceId, now, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone decided the task meanwhile; the next run sees the fresh state.
            }
        }

        await transaction.CommitAsync(ct);
        return fired;
    }

    private async Task<int> EscalateAsync(Guid tenantId, Guid instanceId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
        context.SetTenant(tenantId);
        context.SetUser("system:workflow-escalations", "Escalation scheduler", [RecuroSystemRoles.Service]);
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null)
        {
            return 0;
        }

        var fired = instance.FireDueEscalations(now);
        if (fired > 0)
        {
            await db.SaveChangesAsync(ct);
            Escalated(logger, fired, instanceId, tenantId);
        }

        return fired;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Escalation run failed")]
    private static partial void RunFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fired {Count} escalation step(s) on workflow {InstanceId} (tenant {TenantId})")]
    private static partial void Escalated(ILogger logger, int count, Guid instanceId, Guid tenantId);
}

/// <summary>Role names used by the service's own background work.</summary>
internal static class RecuroSystemRoles
{
    public const string Service = "service";
}
