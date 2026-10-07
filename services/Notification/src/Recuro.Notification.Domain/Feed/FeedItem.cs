using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Domain.Matrix;

namespace Recuro.Notification.Domain.Feed;

/// <summary>
/// One in-app notification (the bell). It is addressed either to one user, or, for HR staff roles only,
/// to everyone holding a role. Items never change after creation; read state lives in
/// <see cref="ReadReceipt"/> so a role-wide item can be read by each person separately.
/// </summary>
public sealed class FeedItem : Entity, ITenantOwned
{
    private FeedItem()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Global, increasing position. The live stream resumes after it (Last-Event-ID).</summary>
    public long Sequence { get; private set; }

    public string RecipientRole { get; private set; } = string.Empty;

    /// <summary>Null for a role-wide item.</summary>
    public string? RecipientUserId { get; private set; }

    /// <summary>Dedupe key with the event id and template: the user id, or <c>role:&lt;role&gt;</c>.</summary>
    public string RecipientKey { get; private set; } = string.Empty;

    public string Icon { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string? Link { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public string MatrixVersion { get; private set; } = string.Empty;

    public bool Critical { get; private set; }

    public Guid SourceEventId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<FeedItem> Create(Recipient recipient, RenderedNotification content, DeliveryOrigin origin, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(origin);
        var check = recipient.EnsureAddressable();
        if (check.IsFailure)
        {
            return check.Error!;
        }

        return new FeedItem
        {
            Id = Guid.CreateVersion7(),
            RecipientRole = recipient.Role,
            RecipientUserId = recipient.UserId,
            RecipientKey = recipient.Key,
            Icon = content.Icon,
            Title = content.Title,
            Body = content.Body,
            Link = content.Link,
            TemplateKey = origin.TemplateKey,
            MatrixVersion = origin.MatrixVersion,
            Critical = origin.Critical,
            SourceEventId = origin.EventId,
            CreatedAt = now,
        };
    }

    /// <summary>True when <paramref name="userId"/> holding <paramref name="roles"/> may see this item.</summary>
    public bool IsVisibleTo(string userId, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return RecipientUserId is null ? roles.Contains(RecipientRole) : RecipientUserId == userId;
    }
}

/// <summary>What the matrix rendered for the bell.</summary>
public sealed record RenderedNotification(string Icon, string Title, string Body, string? Link);

/// <summary>Which event, rule and matrix version produced a delivery (RCU-NTF-004).</summary>
public sealed record DeliveryOrigin(Guid EventId, string EventType, string TemplateKey, string TemplateVersion, string MatrixVersion, bool Critical)
{
    public static DeliveryOrigin From(Guid eventId, MatrixRule rule, string templateVersion)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new(eventId, rule.EventType, rule.TemplateKey, templateVersion, NotificationMatrix.Version, rule.Critical);
    }
}
