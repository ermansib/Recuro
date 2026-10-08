using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Recuro.Careers.Application;
using Recuro.Careers.Application.Abstractions;

namespace Recuro.Careers.Infrastructure.Clients;

/// <summary>
/// RCU-CAR-001: public search pages in Valkey/Redis (or memory when none is configured) for
/// <see cref="CareersOptions.SearchCacheFor"/>. A cache outage only costs a database read.
/// </summary>
internal sealed class JobSearchCache(IDistributedCache cache, IOptions<CareersOptions> options) : IJobSearchCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
        where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct)
        where T : class
    {
        try
        {
            await cache.SetAsync(
                key,
                JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.SearchCacheFor },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort: the next request reads the database again.
        }
    }
}
