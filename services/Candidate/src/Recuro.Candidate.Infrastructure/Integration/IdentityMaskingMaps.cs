using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Application.Candidates.Masking;

namespace Recuro.Candidate.Infrastructure.Integration;

public sealed class IdentityServiceOptions
{
    public const string SectionName = "Services:Identity";

    public Uri BaseUrl { get; set; } = new("http://localhost:5101/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>architecture.md: callers cache a map for up to 5 minutes.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the last good map stays usable while Identity can't be reached.</summary>
    public TimeSpan KeepStaleFor { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Identity's <c>GET /api/v1/identity/masking/{role}/{resource}</c> response (architecture.md).</summary>
internal sealed record MaskingMapResponse(string Resource, string Role, string Version, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// RCU-AUT-004 masking maps from the Identity service, cached per tenant and role. A 404 fails closed.
/// When Identity can't be reached the last good map is used, and with nothing cached the local FRD §3.2
/// table (<see cref="CandidateMasking.LocalFallback"/>), so reads keep working during an Identity outage.
/// </summary>
internal sealed partial class IdentityMaskingMaps(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<IdentityServiceOptions> options,
    ILogger<IdentityMaskingMaps> logger) : IMaskingMaps
{
    private sealed record Entry(IReadOnlyDictionary<string, MaskStrategy>? Map, DateTimeOffset FetchedAt);

    public async Task<IReadOnlyDictionary<string, MaskStrategy>?> GetAsync(string role, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var key = $"candidate:masking:{tenant.TenantId}:{role}";
        var now = clock.GetUtcNow();
        if (cache.TryGetValue(key, out Entry? cached) && cached is not null && now - cached.FetchedAt < options.Value.CacheFor)
        {
            return cached.Map;
        }

        try
        {
            using var response = await http.GetAsync(
                new Uri($"api/v1/identity/masking/{Uri.EscapeDataString(role)}/{CandidateMasking.Resource}", UriKind.Relative), ct);
            IReadOnlyDictionary<string, MaskStrategy>? map;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                map = null;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<MaskingMapResponse>(ct)
                    ?? throw new InvalidOperationException("Identity returned an empty masking map.");
                map = CandidateMasking.Parse(body.Fields);
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
            return CandidateMasking.LocalFallback(role);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity masking map for {Role} unavailable; using the last cached map")]
    private static partial void UsingStale(ILogger logger, string role, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity masking map for {Role} unavailable and not cached; using the local FRD table")]
    private static partial void UsingLocal(ILogger logger, string role, Exception ex);
}
