using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Locking;
using Recuro.Interview.Application.Interviews.Commands;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Infrastructure.Persistence;

namespace Recuro.Interview.Infrastructure.Jobs;

public sealed class FeedbackSlaOptions
{
    public const string SectionName = "FeedbackSla";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// RCU-INT-004 feedback SLA scanner. Each tick raises the reminder and the overdue escalation, once each,
/// for every tenant's rounds still waiting for feedback. One replica at a time (advisory lock, BNFR-4).
/// </summary>
public sealed partial class FeedbackSlaJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<FeedbackSlaOptions> options,
    ILogger<FeedbackSlaJob> logger) : BackgroundService
{
    private const string LockName = "interview:feedback-sla";

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
        var leaseDb = leaseScope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        await using var lease = await leaseDb.Database.BeginTransactionAsync(ct);
        if (!await PostgresLocks.TryLockAsync(leaseDb, LockName, ct))
        {
            return false;
        }

        // System job: the one place allowed to list tenants; each scan then runs inside its tenant's scope.
        var now = clock.GetUtcNow();
        var tenants = await leaseDb.Interviews.IgnoreQueryFilters()
            .Where(i => i.Status == InterviewStatus.Scheduled
                && ((i.ReminderSentAt == null && i.ReminderAt <= now) || (i.OverdueRaisedAt == null && i.OverdueAt <= now)))
            .Select(i => i.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
            context.SetTenant(tenantId);
            context.SetUser("system:feedback-sla", "Feedback SLA scanner", []);
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ScanFeedbackSlaCommand, int>>();
            var result = await handler.Handle(new ScanFeedbackSlaCommand(), ct);
            if (result.IsSuccess && result.Value > 0)
            {
                Raised(logger, tenantId, result.Value);
            }
        }

        await lease.CommitAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Feedback SLA scan failed")]
    private static partial void ScanFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feedback SLA scan raised {Count} reminders or escalations for tenant {TenantId}")]
    private static partial void Raised(ILogger logger, Guid tenantId, int count);
}
