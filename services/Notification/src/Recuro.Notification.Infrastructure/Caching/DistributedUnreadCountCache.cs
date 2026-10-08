using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Feed;

namespace Recuro.Notification.Infrastructure.Caching;

/// <summary>
/// Unread counts in Valkey (or memory) for a short time. The caller's own read actions invalidate
/// their entry; new items show up when it expires, and live streams push them in the meantime.
/// </summary>
internal sealed class DistributedUnreadCountCache(IDistributedCache cache, ITenantContext tenant) : IUnreadCountCache
{
    private static readonly DistributedCacheEntryOptions Lifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15) };

    public async Task<UnreadCountDto?> GetAsync(Viewer viewer, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(Key(viewer), ct);
        return bytes is null ? null : JsonSerializer.Deserialize<UnreadCountDto>(bytes);
    }

    public Task SetAsync(Viewer viewer, UnreadCountDto counts, CancellationToken ct) =>
        cache.SetAsync(Key(viewer), JsonSerializer.SerializeToUtf8Bytes(counts), Lifetime, ct);

    public Task InvalidateAsync(Viewer viewer, CancellationToken ct) => cache.RemoveAsync(Key(viewer), ct);

    private string Key(Viewer viewer) => $"unread:{tenant.RequiredTenantId}:{viewer.UserId}";
}
