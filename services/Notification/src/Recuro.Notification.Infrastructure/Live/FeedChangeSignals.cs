using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Infrastructure.Persistence;

namespace Recuro.Notification.Infrastructure.Live;

/// <summary>
/// Live-update fan-out across replicas with PostgreSQL LISTEN/NOTIFY. A writer calls
/// <c>pg_notify</c> inside its transaction, so PostgreSQL delivers the signal only after commit;
/// every replica listens and wakes the streams of that tenant. The database stays the source of
/// truth: a woken stream reads what is new, so a missed signal costs latency, never data.
/// </summary>
internal static class FeedChannel
{
    public const string Name = "recuro_notification_feed";
}

internal sealed class PostgresFeedChangeSignal(NotificationDbContext db) : IFeedChangeSignal
{
    public Task SignalAsync(Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_notify({FeedChannel.Name}, {tenantId.ToString()})", ct);
}

/// <summary>Per-tenant wake-ups for live streams on this replica.</summary>
public sealed class FeedChangeListener : IFeedChangeListener
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiters = new();

    public async Task<bool> WaitAsync(Guid tenantId, TimeSpan timeout, CancellationToken ct)
    {
        var signal = _waiters.GetOrAdd(tenantId, _ => NewSource()).Task;
        try
        {
            await signal.WaitAsync(timeout, ct);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Wakes every stream of <paramref name="tenantId"/> on this replica.</summary>
    public void Notify(Guid tenantId)
    {
        if (_waiters.TryRemove(tenantId, out var source))
        {
            source.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Holds one LISTEN connection per replica and reconnects if it drops.</summary>
internal sealed partial class FeedChangeListenerService(
    FeedChangeListener listener,
    IConfiguration configuration,
    ILogger<FeedChangeListenerService> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, e) =>
                {
                    if (Guid.TryParse(e.Payload, CultureInfo.InvariantCulture, out var tenantId))
                    {
                        listener.Notify(tenantId);
                    }
                };

                await using (var listen = new NpgsqlCommand($"LISTEN {FeedChannel.Name}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ListenFailed(logger, ex);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live-update listener lost its database connection; reconnecting")]
    private static partial void ListenFailed(ILogger logger, Exception ex);
}
