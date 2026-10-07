using Recuro.BuildingBlocks.Domain;

namespace Recuro.Notification.Domain.Feed;

/// <summary>A user has read a feed item or an email. One row per (item, user); absence means unread.</summary>
public sealed class ReadReceipt : Entity, ITenantOwned
{
    private ReadReceipt()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ItemId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateTimeOffset ReadAt { get; private set; }

    public static ReadReceipt For(Guid itemId, string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new ReadReceipt { Id = Guid.CreateVersion7(), ItemId = itemId, UserId = userId, ReadAt = now };
    }
}
