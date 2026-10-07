using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Recuro.BuildingBlocks.Infrastructure.Outbox;

/// <summary>An integration event waiting to be relayed to the broker (RCU-PLT-002).</summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid id, Guid tenantId, string type, string envelope, DateTimeOffset occurredAt)
    {
        Id = id;
        TenantId = tenantId;
        Type = type;
        Envelope = envelope;
        OccurredAt = occurredAt;
    }

    /// <summary>Same as the CloudEvents id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Kept for operations; the outbox is read by the relay across tenants, so it is not query-filtered.</summary>
    public Guid TenantId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    /// <summary>The CloudEvents 1.0 JSON document, exactly as it will be sent.</summary>
    public string Envelope { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Set when the relay gives up; the row then needs an operator.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    public string? LastError { get; private set; }

    internal void MarkSent(DateTimeOffset now) => ProcessedAt = now;

    internal void MarkAttemptFailed(string error, DateTimeOffset now, int maxAttempts, TimeSpan retryIn)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (Attempts >= maxAttempts)
        {
            FailedAt = now;
        }
        else
        {
            NextAttemptAt = now + retryIn;
        }
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(200);
        builder.Property(m => m.Envelope).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL AND failed_at IS NULL");
    }
}
