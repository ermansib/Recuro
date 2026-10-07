namespace Recuro.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Bound from the <c>Messaging</c> configuration section.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>The single topic exchange every service publishes to. Routing key = event type.</summary>
    public const string Exchange = "recuro.events";

    /// <summary>Off in unit/integration tests that do not need a broker; the outbox still fills.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>AMQP URI, e.g. <c>amqp://recuro:recuro@localhost:5672/</c>. Prefer ConnectionStrings:rabbitmq.</summary>
    public string? ConnectionString { get; set; }

    public int PrefetchCount { get; set; } = 16;

    /// <summary>In-process attempts per message before it is dead-lettered.</summary>
    public int ConsumerAttempts { get; set; } = 3;

    public int OutboxBatchSize { get; set; } = 50;

    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Relay attempts per outbox row before it is marked failed for an operator.</summary>
    public int OutboxMaxAttempts { get; set; } = 10;
}
