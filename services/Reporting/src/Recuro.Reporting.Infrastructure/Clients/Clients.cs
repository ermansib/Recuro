using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Domain.Kpis;

namespace Recuro.Reporting.Infrastructure.Clients;

/// <summary>Base addresses of the services this one calls directly (inside the network, not via the gateway).</summary>
public sealed class ServiceEndpoints
{
    public const string SectionName = "Services";

    /// <summary>Config service (TAT and DOA matrices), e.g. <c>http://localhost:5102/</c>.</summary>
    public Uri? Config { get; set; }

    /// <summary>Identity service (masking maps), e.g. <c>http://localhost:5101/</c>.</summary>
    public Uri? Identity { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long resolved matrices and masking maps are reused (architecture.md: up to 5 minutes).</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the last good answer stays usable while the other service can't be reached.</summary>
    public TimeSpan KeepStaleFor { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Points a client at a service's base address with the configured timeout.</summary>
    public void Apply(HttpClient client, Uri? address, string name)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.BaseAddress = address ?? throw new InvalidOperationException($"Services:{name} is not set.");
        client.Timeout = Timeout;
    }
}

/// <summary>Config's <c>resolve/matrices/{type}</c> response.</summary>
internal sealed record MatrixResponse<TContent>(string ConfigVersionId, string MatrixType, int Number, TContent Content);

internal sealed record TatMatrixContent(IReadOnlyList<TatStageContent>? Stages);

internal sealed record TatStageContent(string Stage, int MaxWorkingDays);

internal sealed record DoaMatrixContent(IReadOnlyList<DoaRouteContent>? Routes);

internal sealed record DoaRouteContent(string Grade, TatRangeContent? OverallTat);

internal sealed record TatRangeContent(int MinDays, int MaxDays);

/// <summary>
/// TAT targets from Config's TAT and DOA matrices (architecture.md, "Config: matrices and versions"),
/// cached per tenant and day. Calls made while serving a user carry that user's token; the schedule's
/// calls get a service token (RCU-AUT-005). If Config is down, the last good targets are used, and with
/// none cached the FRD values, so a report still renders; the snapshot then records no Config version.
/// </summary>
public sealed partial class ConfigTatTargets(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<ServiceEndpoints> options,
    ILogger<ConfigTatTargets> logger) : ITatTargetsSource
{
    private sealed record Entry(TatTargets Targets, DateTimeOffset FetchedAt);

    public async Task<TatTargets> GetAsync(DateTimeOffset at, CancellationToken ct)
    {
        var day = at.UtcDateTime.Date;
        var key = $"reporting:tat:{tenant.TenantId}:{day:yyyyMMdd}";
        if (cache.TryGetValue(key, out Entry? cached) && cached is not null && clock.GetUtcNow() - cached.FetchedAt < options.Value.CacheFor)
        {
            return cached.Targets;
        }

        try
        {
            var when = Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture));
            var tat = await GetAsync<TatMatrixContent>($"api/v1/resolve/matrices/tat?at={when}", ct);
            var doa = await GetAsync<DoaMatrixContent>($"api/v1/resolve/matrices/doa?at={when}", ct);
            var targets = new TatTargets(
                (doa.Content.Routes ?? [])
                    .Where(r => r.OverallTat is not null)
                    .GroupBy(r => r.Grade, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().OverallTat!.MaxDays, StringComparer.OrdinalIgnoreCase),
                (tat.Content.Stages ?? [])
                    .GroupBy(s => s.Stage, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().MaxWorkingDays, StringComparer.OrdinalIgnoreCase),
                $"tat:{tat.ConfigVersionId};doa:{doa.ConfigVersionId}");
            cache.Set(key, new Entry(targets, clock.GetUtcNow()), options.Value.KeepStaleFor);
            return targets;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            if (cached is not null)
            {
                UsingStale(logger, ex);
                return cached.Targets;
            }

            UsingFrd(logger, ex);
            return TatTargets.FrdFallback;
        }
    }

    private async Task<MatrixResponse<T>> GetAsync<T>(string uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri(uri, UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MatrixResponse<T>>(ct)
            ?? throw new InvalidOperationException("Config returned an empty matrix.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config TAT matrices unavailable; using the last cached targets")]
    private static partial void UsingStale(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config TAT matrices unavailable and not cached; using the FRD §5.2 targets")]
    private static partial void UsingFrd(ILogger logger, Exception ex);
}

/// <summary>Identity's <c>GET /api/v1/identity/masking/{role}/{resource}</c> response (architecture.md).</summary>
internal sealed record MaskingMapResponse(string Resource, string Role, string Version, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// RCU-AUT-004 masking maps for the <c>report</c> resource, cached per tenant and role. A 404 fails closed.
/// When Identity can't be reached the last good map is used, and with nothing cached the local FRD §3.2
/// table (<see cref="ReportMasking.LocalFallback"/>).
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
        var key = $"reporting:masking:{tenant.TenantId}:{role}";
        if (cache.TryGetValue(key, out Entry? cached) && cached is not null && clock.GetUtcNow() - cached.FetchedAt < options.Value.CacheFor)
        {
            return cached.Map;
        }

        try
        {
            using var response = await http.GetAsync(
                new Uri($"api/v1/identity/masking/{Uri.EscapeDataString(role)}/{ReportMasking.Resource}", UriKind.Relative), ct);
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

            cache.Set(key, new Entry(map, clock.GetUtcNow()), options.Value.KeepStaleFor);
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
            return ReportMasking.LocalFallback(role);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity report masking map for {Role} unavailable; using the last cached map")]
    private static partial void UsingStale(ILogger logger, string role, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity report masking map for {Role} unavailable and not cached; using the local FRD table")]
    private static partial void UsingLocal(ILogger logger, string role, Exception ex);
}
