using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Recuro.Admin.Api.Auth;

internal static class AuthSetup
{
    /// <summary>
    /// Auth:Mode "Oidc" validates bearer tokens from any OpenID Connect provider (Keycloak, Entra ID, ...).
    /// Auth:Mode "Development" uses the persona header and is refused outside Development and Testing.
    /// </summary>
    public static IServiceCollection AddAdminAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var mode = configuration["Auth:Mode"] ?? "Oidc";
        var useDevelopment = string.Equals(mode, "Development", StringComparison.OrdinalIgnoreCase);

        if (useDevelopment && !(environment.IsDevelopment() || environment.IsEnvironment("Testing")))
        {
            throw new InvalidOperationException("Development sign-in can only be used in the Development or Testing environment.");
        }

        if (useDevelopment)
        {
            services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
        }
        else
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = configuration["Auth:Authority"];
                    options.Audience = configuration["Auth:Audience"];
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.RoleClaimType = configuration["Auth:RoleClaim"] ?? "roles";
                    options.TokenValidationParameters.NameClaimType = "name";
                });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicies.Platform, policy => policy.RequireRole(AdminRoles.PlatformAdmin))
            .AddPolicy(AdminPolicies.Tenant, policy => policy.RequireRole(AdminRoles.TenantAdmin).RequireClaim(AdminClaims.Tenant));

        return services;
    }
}
