using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Recuro.BuildingBlocks.Infrastructure.Inbox;

/// <summary>Records that a handler processed an event, so redelivery is a no-op (consumers dedupe on eventId).</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    public InboxMessage(Guid eventId, string consumer, string type, DateTimeOffset processedAt)
    {
        EventId = eventId;
        Consumer = consumer;
        Type = type;
        ProcessedAt = processedAt;
    }

    public Guid EventId { get; private set; }

    /// <summary>Handler name: one event can be processed by several handlers in the same service.</summary>
    public string Consumer { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(m => new { m.EventId, m.Consumer });
        builder.Property(m => m.Consumer).HasMaxLength(300);
        builder.Property(m => m.Type).HasMaxLength(200);
    }
}
