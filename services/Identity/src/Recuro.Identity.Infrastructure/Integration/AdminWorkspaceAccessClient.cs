using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Access;

namespace Recuro.Identity.Infrastructure.Integration;

/// <summary>The "Services:Admin" section: where the admin API (which owns workspace roles) is.</summary>
public sealed class AdminServiceOptions
{
    public const string SectionName = "Services:Admin";

    /// <summary>False keeps the seeded FRD §3.2 policy for every workspace (runs without the admin API).</summary>
    public bool Enabled { get; set; } = true;

    public Uri BaseUrl { get; set; } = new("http://localhost:5080/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Same as a decision's TTL (RCU-AUT-003): a role change applies within a minute.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long the last good copy is used while the admin API can't be reached.</summary>
    public TimeSpan KeepStaleFor { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>The admin API's <c>GET /api/v1/internal/workspaces/{id}/access</c> response.</summary>
internal sealed record WorkspaceAccessResponse(Guid WorkspaceId, string Version, IReadOnlyList<AccessRoleResponse> Roles, IReadOnlyList<AccessMemberResponse> Members);

internal sealed record AccessRoleResponse(string Key, IReadOnlyList<string> Permissions, bool RequiresMfa, bool IsActive);

internal sealed record AccessMemberResponse(string AccountId, IReadOnlyList<string> RoleKeys);

/// <summary>
/// Reads a workspace's roles from the admin API as this service (service-account token, RCU-AUT-005) and caches
/// them per workspace. When the admin API is down the last good copy is used; with none, null (the seeded policy).
/// </summary>
internal sealed partial class AdminWorkspaceAccessClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<AdminServiceOptions> options,
    ILogger<AdminWorkspaceAccessClient> logger) : IWorkspaceAccessSource
{
    private readonly AdminServiceOptions _options = options.Value;

    public async Task<WorkspaceAccess?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var freshKey = $"identity:workspace-access:{tenantId}";
        if (cache.TryGetValue(freshKey, out WorkspaceAccess? fresh))
        {
            return fresh;
        }

        var staleKey = $"{freshKey}:stale";
        try
        {
            using var response = await http.GetAsync(new Uri($"api/v1/internal/workspaces/{tenantId}/access", UriKind.Relative), ct);
            WorkspaceAccess? access = null;
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<WorkspaceAccessResponse>(ct);
                access = body is null || body.Roles.Count == 0 ? null : ToDomain(body);
            }

            cache.Set(freshKey, access, _options.CacheFor);
            cache.Set(staleKey, access, _options.KeepStaleFor);
            return access;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            LogUnavailable(logger, tenantId, ex);
            return cache.TryGetValue(staleKey, out WorkspaceAccess? stale) ? stale : null;
        }
    }

    private static WorkspaceAccess ToDomain(WorkspaceAccessResponse body) => new(
        body.Version,
        body.Roles.Select(r => new WorkspaceRoleDefinition(r.Key, r.Permissions, r.RequiresMfa, r.IsActive)).ToList(),
        body.Members.ToDictionary(m => m.AccountId, m => m.RoleKeys, StringComparer.Ordinal));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read workspace {TenantId}'s roles from the admin API; using the last known or seeded policy")]
    private static partial void LogUnavailable(ILogger logger, Guid tenantId, Exception exception);
}
