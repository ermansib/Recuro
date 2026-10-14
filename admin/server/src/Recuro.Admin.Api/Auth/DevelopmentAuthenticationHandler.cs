using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Recuro.Admin.Api.Auth;

/// <summary>
/// Development-only sign-in that stands in for the real identity provider. The client sends
/// "X-Recuro-Persona: platform", "X-Recuro-Persona: tenant:{tenantId}" or, for a main-portal person,
/// "X-Recuro-Persona: person:{tenantId}:{accountId}:{role,role}" and gets the same claims a real token would carry.
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
    private const string PersonPrefix = "person:";

    /// <summary>What a Recuro microservice sends in Auth:Mode=Development instead of a service-account token.</summary>
    public const string DevRolesHeader = "X-Dev-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers[DevRolesHeader].ToString().Split(',', StringSplitOptions.TrimEntries).Contains(AdminRoles.Service))
        {
            var service = new ClaimsIdentity([new(ClaimTypes.Name, "Recuro service"), new(ClaimTypes.Role, AdminRoles.Service)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(service), SchemeName)));
        }

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
                new(ClaimTypes.Name, "Workspace admin"),
                new(ClaimTypes.Role, AdminRoles.TenantAdmin),
                new(AdminClaims.Tenant, tenantId.ToString()),
            ];
        }
        else if (persona.StartsWith(PersonPrefix, StringComparison.Ordinal)
            && persona[PersonPrefix.Length..].Split(':', 3) is [var tenant, var account, var roles]
            && Guid.TryParse(tenant, out var personTenant) && account.Length > 0)
        {
            claims =
            [
                new(ClaimTypes.Name, "Workspace person"),
                new(AdminClaims.Tenant, personTenant.ToString()),
                new(AdminClaims.Subject, account),
                .. roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => new Claim(AdminClaims.WorkspaceRoles, r)),
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
