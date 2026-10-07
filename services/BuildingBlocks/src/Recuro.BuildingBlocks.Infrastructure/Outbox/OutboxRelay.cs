using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Moves outbox rows to the broker. At-least-once and safe with many replicas: each batch is claimed
/// with <c>FOR UPDATE SKIP LOCKED</c>, so two instances never send the same row at the same time.
/// Consumers dedupe on the event id, so a resend after a crash is harmless.
/// </summary>
public sealed partial class OutboxRelay(
    IServiceScopeFactory scopes,
    IMessageBroker broker,
    TimeProvider clock,
    IOptions<MessagingOptions> options,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int sent;
            try
            {
                sent = await RelayBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RelayFailed(logger, ex);
                sent = 0;
            }

            if (sent == 0)
            {
                await Task.Delay(options.Value.OutboxPollInterval, clock, stoppingToken);
            }
        }
    }

    /// <summary>Sends one batch. Returns how many rows were claimed. Public for tests.</summary>
    public async Task<int> RelayBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecuroDbContext>();
        var now = clock.GetUtcNow();
        var batchSize = options.Value.OutboxBatchSize;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND failed_at IS NULL
                  AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                ORDER BY occurred_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            using var activity = MessagingTelemetry.Source.StartActivity($"publish {message.Type}", ActivityKind.Producer);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.message.id", message.Id.ToString());
            try
            {
                await broker.PublishAsync(message.Type, message.Id, Encoding.UTF8.GetBytes(message.Envelope), ct);
                message.MarkSent(clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkAttemptFailed(ex.Message, clock.GetUtcNow(), options.Value.OutboxMaxAttempts, Backoff.For(message.Attempts));
                PublishFailed(logger, message.Id, message.Type, message.Attempts, ex);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay loop failed")]
    private static partial void RelayFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing event {EventId} ({EventType}) failed, attempt {Attempt}")]
    private static partial void PublishFailed(ILogger logger, Guid eventId, string eventType, int attempt, Exception ex);
}

/// <summary>Exponential backoff with jitter, capped at five minutes (BNFR-4).</summary>
internal static class Backoff
{
    public static TimeSpan For(int attempt)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Min(attempt, 10)));
        var jitter = Random.Shared.NextDouble() * 0.3 * seconds;
        return TimeSpan.FromSeconds(seconds + jitter);
    }
}

/// <summary>The ActivitySource for publish/consume spans. Registered with OpenTelemetry in the web layer.</summary>
public static class MessagingTelemetry
{
    public const string SourceName = "Recuro.Messaging";

    public static readonly ActivitySource Source = new(SourceName);
}
