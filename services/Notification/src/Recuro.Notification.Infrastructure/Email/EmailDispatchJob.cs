using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Notification.Application.Emails.Commands;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Infrastructure.Persistence;

namespace Recuro.Notification.Infrastructure.Email;

public sealed class EmailDispatchOptions
{
    public const string SectionName = "EmailDispatch";

    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 20;

    /// <summary>How long a claimed email is hidden from other replicas while it is being sent.</summary>
    public TimeSpan ClaimFor { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Sends due emails (RCU-NTF-002). Claims a batch across tenants with <c>FOR UPDATE SKIP LOCKED</c>,
/// so replicas never send the same email twice (BNFR-4), then delivers each one inside its tenant's
/// scope. Delivery problems never block the services that raised the events (BNFR-2): mail queues here.
/// </summary>
public sealed partial class EmailDispatchJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<EmailDispatchOptions> options,
    ILogger<EmailDispatchJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollInterval, clock);
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

    /// <summary>Claims and delivers one batch. Returns how many emails were attempted. Public for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var claimed = await ClaimAsync(ct);
        foreach (var (id, tenantId) in claimed)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenantId);
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeliverEmailCommand>>()
                .Handle(new DeliverEmailCommand(id), ct);
            if (result.IsFailure)
            {
                DeliveryFailed(logger, id, result.Error!.Message);
            }
        }

        return claimed.Count;
    }

    private async Task<List<(Guid Id, Guid TenantId)>> ClaimAsync(CancellationToken ct)
    {
        // System job: the one place that reads across tenants, then works inside each tenant's scope.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var now = clock.GetUtcNow();
        var pending = nameof(EmailStatus.Pending);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var due = await db.Emails
            .FromSql($"SELECT * FROM email_messages WHERE status = {pending} AND next_attempt_at <= {now} ORDER BY next_attempt_at LIMIT {options.Value.BatchSize} FOR UPDATE SKIP LOCKED")
            .IgnoreQueryFilters()
            .ToListAsync(ct);
        foreach (var message in due)
        {
            message.Claim(now + options.Value.ClaimFor);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return due.Select(m => (m.Id, m.TenantId)).ToList();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Email dispatch run failed")]
    private static partial void RunFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailId} was not delivered: {Reason}")]
    private static partial void DeliveryFailed(ILogger logger, Guid emailId, string reason);
}
