using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;

namespace Recuro.Notification.Application.Feed.Queries;

/// <summary>RCU-NTF-003: badge counts, cached briefly per tenant and user.</summary>
public sealed record GetUnreadCountQuery : IQuery<UnreadCountDto>;

internal sealed class GetUnreadCountQueryHandler(INotificationReadStore store, IUnreadCountCache cache, ICurrentUser user)
    : IQueryHandler<GetUnreadCountQuery, UnreadCountDto>
{
    public async Task<Result<UnreadCountDto>> Handle(GetUnreadCountQuery query, CancellationToken ct)
    {
        var viewer = ViewerFrom.Current(user);
        if (viewer.IsFailure)
        {
            return viewer.Error!;
        }

        if (await cache.GetAsync(viewer.Value, ct) is { } cached)
        {
            return cached;
        }

        var counts = await store.CountUnreadAsync(viewer.Value, ct);
        await cache.SetAsync(viewer.Value, counts, ct);
        return counts;
    }
}
