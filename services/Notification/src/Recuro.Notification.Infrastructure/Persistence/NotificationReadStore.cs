using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Emails;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Infrastructure.Persistence;

/// <summary>
/// Read side for the bell and the email centre. The tenant filter scopes every query; on top of it a
/// viewer sees items addressed to them, plus role-wide items for the roles they hold.
/// </summary>
internal sealed class NotificationReadStore(NotificationDbContext db) : INotificationReadStore
{
    public async Task<IReadOnlyList<FeedEntry>> ListFeedAsync(Viewer viewer, FeedFilter filter, CancellationToken ct)
    {
        var query = VisibleFeed(viewer);
        if (filter.Before is { } before)
        {
            var cursor = await db.FeedItems.AsNoTracking().Where(i => i.Id == before).Select(i => (long?)i.Sequence).FirstOrDefaultAsync(ct);
            query = cursor is { } sequence ? query.Where(i => i.Sequence < sequence) : query.Where(_ => false);
        }

        return await ProjectFeed(query.OrderByDescending(i => i.Sequence), viewer, filter.UnreadOnly, filter.Limit, ct);
    }

    public Task<IReadOnlyList<FeedEntry>> ListFeedSinceAsync(Viewer viewer, long afterSequence, int limit, CancellationToken ct) =>
        ProjectFeed(VisibleFeed(viewer).Where(i => i.Sequence > afterSequence).OrderBy(i => i.Sequence), viewer, unreadOnly: false, limit, ct);

    public async Task<long> LatestSequenceAsync(CancellationToken ct) =>
        await db.FeedItems.AsNoTracking().MaxAsync(i => (long?)i.Sequence, ct) ?? 0;

    public async Task<IReadOnlyList<EmailMessageDto>> ListEmailsAsync(Viewer viewer, FeedFilter filter, CancellationToken ct)
    {
        var query = VisibleEmails(viewer);
        if (filter.Before is { } before)
        {
            var cursor = await db.Emails.AsNoTracking().Where(m => m.Id == before).Select(m => (DateTimeOffset?)m.CreatedAt).FirstOrDefaultAsync(ct);
            query = cursor is { } at ? query.Where(m => m.CreatedAt < at) : query.Where(_ => false);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new { Message = m, Read = db.ReadReceipts.Any(r => r.ItemId == m.Id && r.UserId == viewer.UserId) })
            .Where(x => !filter.UnreadOnly || !x.Read)
            .Take(filter.Limit)
            .ToListAsync(ct);
        return rows.Select(x => EmailMessageDto.FromMessage(x.Message, !x.Read)).ToList();
    }

    public async Task<UnreadCountDto> CountUnreadAsync(Viewer viewer, CancellationToken ct)
    {
        var notifications = await VisibleFeed(viewer).CountAsync(i => !db.ReadReceipts.Any(r => r.ItemId == i.Id && r.UserId == viewer.UserId), ct);
        var emails = await VisibleEmails(viewer).CountAsync(m => !db.ReadReceipts.Any(r => r.ItemId == m.Id && r.UserId == viewer.UserId), ct);
        return new UnreadCountDto(notifications, emails);
    }

    public async Task<IReadOnlyList<Guid>> UnreadIdsAsync(Viewer viewer, bool emails, CancellationToken ct) => emails
        ? await VisibleEmails(viewer).Where(m => !db.ReadReceipts.Any(r => r.ItemId == m.Id && r.UserId == viewer.UserId)).Select(m => m.Id).ToListAsync(ct)
        : await VisibleFeed(viewer).Where(i => !db.ReadReceipts.Any(r => r.ItemId == i.Id && r.UserId == viewer.UserId)).Select(i => i.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> VisibleIdsAsync(Viewer viewer, IReadOnlyCollection<Guid> ids, bool emails, CancellationToken ct)
    {
        var wanted = ids.ToArray();
        return emails
            ? await VisibleEmails(viewer).Where(m => wanted.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct)
            : await VisibleFeed(viewer).Where(i => wanted.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryLogEntryDto>> ListDeliveryLogAsync(DeliveryLogFilter filter, CancellationToken ct)
    {
        var query = db.Emails.AsNoTracking();
        if (filter.Status is not null && Enum.TryParse<EmailStatus>(filter.Status, ignoreCase: true, out var status))
        {
            query = query.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.TemplateKey))
        {
            query = query.Where(m => m.TemplateKey == filter.TemplateKey);
        }

        if (filter.SourceEventId is { } eventId)
        {
            query = query.Where(m => m.SourceEventId == eventId);
        }

        var rows = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(filter.Limit).ToListAsync(ct);
        return rows.Select(DeliveryLogEntryDto.From).ToList();
    }

    private async Task<IReadOnlyList<FeedEntry>> ProjectFeed(IQueryable<FeedItem> ordered, Viewer viewer, bool unreadOnly, int limit, CancellationToken ct)
    {
        var rows = await ordered
            .Select(i => new { Item = i, Read = db.ReadReceipts.Any(r => r.ItemId == i.Id && r.UserId == viewer.UserId) })
            .Where(x => !unreadOnly || !x.Read)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(x => new FeedEntry(x.Item.Sequence, AppNotificationDto.From(x.Item, !x.Read))).ToList();
    }

    private IQueryable<FeedItem> VisibleFeed(Viewer viewer) => db.FeedItems.AsNoTracking().Where(VisibleTo<FeedItem>(viewer));

    private IQueryable<EmailMessage> VisibleEmails(Viewer viewer) => db.Emails.AsNoTracking().Where(VisibleTo<EmailMessage>(viewer));

    /// <summary>Addressed to the viewer, or role-wide for a role they hold. Mirrors <see cref="FeedItem.IsVisibleTo"/>.</summary>
    private static Expression<Func<T, bool>> VisibleTo<T>(Viewer viewer)
    {
        var userId = viewer.UserId;
        var roles = viewer.Roles.ToArray();
        return item => EF.Property<string?>(item!, nameof(FeedItem.RecipientUserId)) == userId
            || (EF.Property<string?>(item!, nameof(FeedItem.RecipientUserId)) == null
                && roles.Contains(EF.Property<string>(item!, nameof(FeedItem.RecipientRole))));
    }
}
