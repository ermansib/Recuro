using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;

namespace Recuro.BuildingBlocks.Web.Auth;

/// <summary>
/// Service-to-service credentials (RCU-AUT-005), bound from the <c>ServiceAuth</c> section. Each service
/// is a confidential Keycloak client <c>recuro-svc-{name}</c> whose service account holds the
/// <c>service</c> role and nothing else.
/// </summary>
public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    /// <summary>The named HttpClient that calls the token endpoint.</summary>
    public const string HttpClientName = "recuro-service-token";

    /// <summary>Defaults to <c>recuro-svc-{Service:Name}</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// From a vault or environment variable (<c>ServiceAuth__ClientSecret</c>). In the Development
    /// environment it defaults to the realm file's local secret, <c>{ClientId}-dev-secret</c>.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Defaults to the realm's token endpoint, next to <c>Auth:MetadataAddress</c> when set, else
    /// next to <c>Auth:Authority</c>.
    /// </summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>A cached token is renewed this long before it expires.</summary>
    public TimeSpan RenewBefore { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// The header a service-account caller uses to say which tenant a call is for. A service token carries no
/// tenant of its own (one service serves every tenant), so the callee takes the tenant from this header,
/// and only when the token holds the <c>service</c> role and no <c>tenant_id</c> claim.
/// </summary>
public static class ServiceTenantHeader
{
    public const string Name = "X-Recuro-Tenant";

    /// <summary>JWT bearer hook: gives a validated service-account token the tenant named in <see cref="Name"/>.</summary>
    public static Task ApplyAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity
            && identity.HasClaim(RecuroClaims.Roles, RecuroRoles.Service)
            && !identity.HasClaim(c => c.Type == RecuroClaims.Tenant)
            && Guid.TryParse(context.Request.Headers[Name].ToString(), out var tenant)
            && tenant != Guid.Empty)
        {
            identity.AddClaim(new System.Security.Claims.Claim(RecuroClaims.Tenant, tenant.ToString()));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Gets this service's own access token (OAuth2 client credentials).</summary>
public interface IServiceTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);

    /// <summary>Drops the cached token, e.g. after a 401 when the secret or signing key was rotated.</summary>
    void Invalidate();
}

/// <summary>
/// Client-credentials tokens from Keycloak, cached until shortly before they expire. Options are read on
/// every renewal, so a rotated secret takes effect without a restart.
/// </summary>
internal sealed partial class KeycloakServiceTokenProvider(
    IHttpClientFactory httpClients,
    IOptionsMonitor<ServiceAuthOptions> options,
    TimeProvider clock,
    ILogger<KeycloakServiceTokenProvider> logger) : IServiceTokenProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Token, DateTimeOffset RenewAt)? _cached;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cached is { } hit && clock.GetUtcNow() < hit.RenewAt)
        {
            return hit.Token;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is { } fresh && clock.GetUtcNow() < fresh.RenewAt)
            {
                return fresh.Token;
            }

            var current = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(current.ClientId) || string.IsNullOrWhiteSpace(current.ClientSecret) || current.TokenEndpoint is null)
            {
                throw new InvalidOperationException("ServiceAuth:ClientId, ServiceAuth:ClientSecret and a token endpoint are required for service-to-service calls.");
            }

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = current.ClientId,
                ["client_secret"] = current.ClientSecret,
            });
            using var response = await httpClients.CreateClient(ServiceAuthOptions.HttpClientName).PostAsync(current.TokenEndpoint, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                TokenRefused(logger, current.ClientId, (int)response.StatusCode);
                throw new HttpRequestException($"The token endpoint refused client '{current.ClientId}'.", null, response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new HttpRequestException("The token endpoint returned an empty response.");
            if (string.IsNullOrWhiteSpace(body.AccessToken) || body.ExpiresIn <= 0)
            {
                throw new HttpRequestException("The token endpoint returned no access token.");
            }

            var lifetime = TimeSpan.FromSeconds(body.ExpiresIn);
            var renewBefore = current.RenewBefore < lifetime / 2 ? current.RenewBefore : lifetime / 2;
            _cached = (body.AccessToken, clock.GetUtcNow() + lifetime - renewBefore);
            return body.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cached = null;

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak refused a client-credentials token for {ClientId} ({Status})")]
    private static partial void TokenRefused(ILogger logger, string clientId, int status);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>
/// Signs outgoing calls that have no caller credentials of their own (event handlers, background jobs)
/// as this service: a client-credentials bearer token plus <see cref="ServiceTenantHeader"/> for the
/// tenant in scope. A call that already carries <c>Authorization</c> or <c>X-Dev-User</c> (a forwarded
/// caller) is left alone, so register this handler after any caller-forwarding handler. In
/// <c>Auth:Mode=Development</c> it sends the X-Dev-* headers instead of fetching a token.
/// </summary>
public sealed class ServiceTokenHandler(
    IServiceTokenProvider tokens,
    IOptions<RecuroAuthOptions> auth,
    IOptions<ServiceAuthOptions> serviceAuth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Headers.Authorization is not null || request.Headers.Contains(DevelopmentAuthenticationHandler.UserHeader))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var tenant = ScopeContext.Current?.TenantId;
        if (auth.Value.IsDevelopmentMode)
        {
            request.Headers.TryAddWithoutValidation(DevelopmentAuthenticationHandler.UserHeader, serviceAuth.Value.ClientId);
            request.Headers.TryAddWithoutValidation(DevelopmentAuthenticationHandler.RolesHeader, RecuroRoles.Service);
            if (tenant is { } devTenant)
            {
                request.Headers.TryAddWithoutValidation(DevelopmentAuthenticationHandler.TenantHeader, devTenant.ToString());
            }

            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        if (tenant is { } id)
        {
            request.Headers.Remove(ServiceTenantHeader.Name);
            request.Headers.TryAddWithoutValidation(ServiceTenantHeader.Name, id.ToString());
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The next call fetches a new token (rotated secret or signing key).
            tokens.Invalidate();
        }

        return response;
    }
}
