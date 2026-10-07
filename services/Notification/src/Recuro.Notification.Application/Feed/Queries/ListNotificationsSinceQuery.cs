using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;

namespace Recuro.Notification.Application.Feed.Queries;

/// <summary>
/// RCU-GTW-003 live stream: the caller's bell items after stream position <see cref="AfterSequence"/>,
/// oldest first. Null means "from now" and returns nothing but the current position.
/// </summary>
public sealed record ListNotificationsSinceQuery(long? AfterSequence) : IQuery<FeedPage>;

/// <summary>Items to send and the position to resume from.</summary>
public sealed record FeedPage(IReadOnlyList<FeedEntry> Entries, long Position);

internal sealed class ListNotificationsSinceQueryHandler(INotificationReadStore store, ICurrentUser user)
    : IQueryHandler<ListNotificationsSinceQuery, FeedPage>
{
    public async Task<Result<FeedPage>> Handle(ListNotificationsSinceQuery query, CancellationToken ct)
    {
        var viewer = ViewerFrom.Current(user);
        if (viewer.IsFailure)
        {
            return viewer.Error!;
        }

        if (query.AfterSequence is not { } after)
        {
            return new FeedPage([], await store.LatestSequenceAsync(ct));
        }

        var entries = await store.ListFeedSinceAsync(viewer.Value, after, NotificationLimits.StreamBatchSize, ct);
        return new FeedPage(entries, entries.Count == 0 ? after : entries[^1].Sequence);
    }
}
