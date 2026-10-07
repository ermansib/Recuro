using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Recuro.BuildingBlocks.Web.Auth;

/// <summary>
/// Local runs and tests without Keycloak: the caller states who they are in X-Dev-* headers.
/// Registration refuses this handler outside the Development and Testing environments.
/// </summary>
public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public const string UserHeader = "X-Dev-User";
    public const string NameHeader = "X-Dev-Name";
    public const string RolesHeader = "X-Dev-Roles";
    public const string TenantHeader = "X-Dev-Tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(RecuroClaims.Subject, user),
            new(RecuroClaims.Name, Request.Headers[NameHeader].FirstOrDefault() ?? user),
        };
        var tenant = Request.Headers[TenantHeader].ToString();
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            claims.Add(new Claim(RecuroClaims.Tenant, tenant));
        }

        claims.AddRange(Request.Headers[RolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim(RecuroClaims.Roles, role)));

        var identity = new ClaimsIdentity(claims, SchemeName, RecuroClaims.Name, RecuroClaims.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
