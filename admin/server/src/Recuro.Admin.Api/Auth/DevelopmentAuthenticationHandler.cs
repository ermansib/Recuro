using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Recuro.Admin.Api.Auth;

/// <summary>
/// Development-only sign-in that stands in for the real identity provider. The client sends
/// "X-Recuro-Persona: platform" or "X-Recuro-Persona: tenant:{tenantId}" and gets the same claims a real token would carry.
/// Never registered outside Development and Testing (see Program.cs).
/// </summary>
internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public const string PersonaHeader = "X-Recuro-Persona";
    private const string TenantPrefix = "tenant:";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PersonaHeader, out var values) || values.ToString() is not { Length: > 0 } persona)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims;
        if (persona == "platform")
        {
            claims = [new(ClaimTypes.Name, "Platform admin"), new(ClaimTypes.Role, AdminRoles.PlatformAdmin)];
        }
        else if (persona.StartsWith(TenantPrefix, StringComparison.Ordinal) && Guid.TryParse(persona[TenantPrefix.Length..], out var tenantId))
        {
            claims =
            [
                new(ClaimTypes.Name, "Tenant admin"),
                new(ClaimTypes.Role, AdminRoles.TenantAdmin),
                new(AdminClaims.Tenant, tenantId.ToString()),
            ];
        }
        else
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown development persona."));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
