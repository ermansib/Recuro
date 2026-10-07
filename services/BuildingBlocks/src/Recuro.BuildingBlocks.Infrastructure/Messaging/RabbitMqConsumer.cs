using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Recuro.BuildingBlocks.Infrastructure.Outbox;

namespace Recuro.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// The service's queue (named after the service) bound to the event types it subscribes to. Failed
/// messages are retried in process with backoff, then dead-lettered to <c>{service}.dlq</c>.
/// </summary>
internal sealed partial class RabbitMqConsumer(
    RabbitMqConnection connection,
    IntegrationEventProcessor processor,
    EventSubscriptions subscriptions,
    IOptions<ServiceIdentity> service,
    IOptions<MessagingOptions> options,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (subscriptions.Items.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Broker not up yet (e.g. docker compose still starting): keep trying.
                StartFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var queue = service.Value.Name;
        var deadLetterExchange = $"{queue}.dlx";
        var deadLetterQueue = $"{queue}.dlq";

        var conn = await connection.GetAsync(ct);
        _channel = await conn.CreateChannelAsync(cancellationToken: ct);
        await RabbitMqConnection.DeclareExchangeAsync(_channel, ct);
        await _channel.ExchangeDeclareAsync(deadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await _channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, string.Empty, cancellationToken: ct);

        var arguments = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = deadLetterExchange };
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments, cancellationToken: ct);
        foreach (var key in subscriptions.BindingKeys)
        {
            await _channel.QueueBindAsync(queue, MessagingOptions.Exchange, key, cancellationToken: ct);
        }

        await _channel.BasicQosAsync(0, (ushort)options.Value.PrefetchCount, global: false, ct);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(_channel, delivery, ct);
        await _channel.BasicConsumeAsync(queue, autoAck: false, consumer, ct);
        Consuming(logger, queue, subscriptions.BindingKeys);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        CloudEvent cloudEvent;
        try
        {
            cloudEvent = EventJson.Deserialize(delivery.Body.Span);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            Unreadable(logger, delivery.BasicProperties.MessageId, ex);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await processor.ProcessAsync(cloudEvent, ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= options.Value.ConsumerAttempts)
                {
                    DeadLettered(logger, cloudEvent.Id, cloudEvent.Type, attempt, ex);
                    await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(100)), ct);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start consuming; retrying")]
    private static partial void StartFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming queue {Queue} bound to {Bindings}")]
    private static partial void Consuming(ILogger logger, string queue, IEnumerable<string> bindings);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} is not a readable CloudEvent; dead-lettered")]
    private static partial void Unreadable(ILogger logger, string? messageId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event {EventId} ({EventType}) failed {Attempts} times; dead-lettered")]
    private static partial void DeadLettered(ILogger logger, Guid eventId, string eventType, int attempts, Exception ex);
}
