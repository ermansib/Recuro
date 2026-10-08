using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Application.Offers;

namespace Recuro.Offer.Infrastructure.Clients;

/// <summary>Identity's <c>GET /api/v1/identity/masking/{role}/{resource}</c> response (architecture.md).</summary>
internal sealed record MaskingMapResponse(string Resource, string Role, string Version, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// RCU-AUT-004 masking maps for the <c>offer</c> resource, cached per tenant and role. A 404 fails closed.
/// When Identity can't be reached the last good map is used, and with nothing cached the local FRD §3.2
/// table (<see cref="OfferMasking.LocalFallback"/>), so reads keep working during an Identity outage.
/// </summary>
public sealed partial class IdentityMaskingMaps(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<ServiceEndpoints> options,
    ILogger<IdentityMaskingMaps> logger) : IMaskingMaps
{
    private sealed record Entry(IReadOnlyDictionary<string, string>? Map, DateTimeOffset FetchedAt);

    public async Task<IReadOnlyDictionary<string, string>?> GetAsync(string role, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var key = $"offer:masking:{tenant.TenantId}:{role}";
        var now = clock.GetUtcNow();
        if (cache.TryGetValue(key, out Entry? cached) && cached is not null && now - cached.FetchedAt < options.Value.CacheFor)
        {
            return cached.Map;
        }

        try
        {
            using var response = await http.GetAsync(
                new Uri($"api/v1/identity/masking/{Uri.EscapeDataString(role)}/{OfferMasking.Resource}", UriKind.Relative), ct);
            IReadOnlyDictionary<string, string>? map;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                map = null;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<MaskingMapResponse>(ct)
                    ?? throw new InvalidOperationException("Identity returned an empty masking map.");
                map = body.Fields;
            }

            cache.Set(key, new Entry(map, now), options.Value.KeepStaleFor);
            return map;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            if (cached is not null)
            {
                UsingStale(logger, role, ex);
                return cached.Map;
            }

            UsingLocal(logger, role, ex);
            return OfferMasking.LocalFallback(role);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity offer masking map for {Role} unavailable; using the last cached map")]
    private static partial void UsingStale(ILogger logger, string role, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity offer masking map for {Role} unavailable and not cached; using the local FRD table")]
    private static partial void UsingLocal(ILogger logger, string role, Exception ex);
}
