using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.Bgv.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Abstractions;

namespace Recuro.Bgv.Infrastructure.Integration;

public sealed class IdentityServiceOptions
{
    public const string SectionName = "Services:Identity";

    public Uri BaseUrl { get; set; } = new("http://localhost:5101/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>architecture.md: callers cache a map for up to 5 minutes.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Identity's <c>GET /api/v1/identity/masking/{role}/{resource}</c> response.</summary>
internal sealed record MaskingMapResponse(string Resource, string Role, string Version, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// RCU-BGV-008 / AUT-004: the <c>sensitiveNote</c> rule for the caller's role from Identity's masking map
/// (resource <c>bgvCheck</c>). When Identity has no map for it (404) or can't be reached, the FRD §3.2/§14
/// table applies: HR-TA and HR Head see sensitive notes, everyone else does not. It never fails open.
/// </summary>
internal sealed partial class IdentityMaskingClient(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    ICurrentUser caller,
    IOptions<IdentityServiceOptions> options,
    ILogger<IdentityMaskingClient> logger) : ISensitiveNotePolicy
{
    public const string Resource = "bgvCheck";
    public const string Field = "sensitiveNote";
    private static readonly HashSet<string> HrRoles = new(StringComparer.Ordinal) { "hrta", "hrhead" };

    public async Task<bool> CallerMaySeeAsync(CancellationToken ct)
    {
        var roles = caller.Roles;
        if (roles.Count == 0)
        {
            return false;
        }

        // Several roles: the most permissive one decides, as for any other masked field.
        foreach (var role in roles)
        {
            if (await RoleMaySeeAsync(role, ct))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool LocalRule(string role) => HrRoles.Contains(role);

    private async Task<bool> RoleMaySeeAsync(string role, CancellationToken ct)
    {
        var key = $"bgv:masking:{tenant.TenantId}:{role}";
        if (cache.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        bool visible;
        try
        {
            using var response = await http.GetAsync(new Uri($"api/v1/identity/masking/{Uri.EscapeDataString(role)}/{Resource}", UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                visible = LocalRule(role);
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var map = await response.Content.ReadFromJsonAsync<MaskingMapResponse>(ct);
                visible = map is not null && !map.Fields.ContainsKey(Field);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            UsingLocal(logger, role, ex);
            return LocalRule(role);
        }

        cache.Set(key, visible, options.Value.CacheFor);
        return visible;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity masking map for {Role} unavailable; using the local FRD rule")]
    private static partial void UsingLocal(ILogger logger, string role, Exception ex);
}
