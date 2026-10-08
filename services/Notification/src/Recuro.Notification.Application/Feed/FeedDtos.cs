using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Application.Feed;

/// <summary>The frontend's <c>AppNotification</c> (frontend/src/domain/types.ts), field for field.</summary>
public sealed record AppNotificationDto(
    string Id,
    string RecipientRole,
    string Icon,
    string Title,
    string Body,
    string CreatedAt,
    bool Unread,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Link)
{
    public static AppNotificationDto From(FeedItem item, bool unread)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id.ToString(), item.RecipientRole, item.Icon, item.Title, item.Body, Timestamps.Format(item.CreatedAt), unread, item.Link);
    }
}

/// <summary>A feed item with its stream position (the SSE event id).</summary>
public sealed record FeedEntry(long Sequence, AppNotificationDto Notification);

/// <summary>Badge counts for the bell and the email centre.</summary>
public sealed record UnreadCountDto(int Notifications, int Emails);

/// <summary>ISO-8601 UTC with seconds, like the mock data (<c>2026-08-18T08:42:00Z</c>).</summary>
public static class Timestamps
{
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
