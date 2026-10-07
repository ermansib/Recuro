using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Recuro.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Sends an already-serialized CloudEvent to the bus. The outbox relay is its only caller.</summary>
public interface IMessageBroker
{
    Task PublishAsync(string routingKey, Guid messageId, ReadOnlyMemory<byte> body, CancellationToken ct);
}

/// <summary>One long-lived connection per process (RabbitMQ.Client recovers it automatically).</summary>
internal sealed partial class RabbitMqConnection(IOptions<MessagingOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.Value.ConnectionString
                    ?? throw new InvalidOperationException("Set ConnectionStrings:rabbitmq or disable Messaging.")),
                AutomaticRecoveryEnabled = true,
                ClientProvidedName = AppDomain.CurrentDomain.FriendlyName,
            };
            _connection = await factory.CreateConnectionAsync(ct);
            Connected(logger, factory.Endpoint);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    /// <summary>Declares the shared topic exchange. Idempotent; every publisher and consumer calls it.</summary>
    public static Task DeclareExchangeAsync(IChannel channel, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(MessagingOptions.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ at {Endpoint}")]
    private static partial void Connected(ILogger logger, AmqpTcpEndpoint endpoint);
}

internal sealed class RabbitMqBroker(RabbitMqConnection connection) : IMessageBroker, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(string routingKey, Guid messageId, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            var properties = new BasicProperties
            {
                MessageId = messageId.ToString(),
                ContentType = CloudEvent.ContentType,
                DeliveryMode = DeliveryModes.Persistent,
                Type = routingKey,
            };

            // Publisher confirms are on: this completes only once the broker has the message.
            await channel.BasicPublishAsync(MessagingOptions.Exchange, routingKey, mandatory: false, properties, body, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var conn = await connection.GetAsync(ct);
        _channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await RabbitMqConnection.DeclareExchangeAsync(_channel, ct);
        return _channel;
    }
}
