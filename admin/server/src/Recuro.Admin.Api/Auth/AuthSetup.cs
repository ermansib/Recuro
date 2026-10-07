using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Recuro.Admin.Api.Auth;

internal static class AuthSetup
{
    /// <summary>
    /// Auth:Mode "Oidc" validates bearer tokens from Keycloak (or any OpenID Connect provider).
    /// Auth:Mode "Development" uses the persona header and is refused outside Development and Testing.
    /// </summary>
    public static IServiceCollection AddAdminAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(AdminAuthOptions.SectionName).Get<AdminAuthOptions>() ?? new AdminAuthOptions();
        services.Configure<AdminAuthOptions>(configuration.GetSection(AdminAuthOptions.SectionName));

        if (options.IsDevelopmentMode)
        {
            if (!(environment.IsDevelopment() || environment.IsEnvironment("Testing")))
            {
                throw new InvalidOperationException("Development sign-in can only be used in the Development or Testing environment.");
            }

            services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.Authority))
            {
                throw new InvalidOperationException("Auth:Authority must be set to the Keycloak realm URL, for example http://localhost:8080/realms/recuro.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt =>
                {
                    jwt.Authority = options.Authority;
                    jwt.Audience = options.Audience;
                    jwt.MapInboundClaims = false;
                    // Keycloak in local development runs on plain http; everywhere else the issuer must be https.
                    jwt.RequireHttpsMetadata = !environment.IsDevelopment();
                    jwt.TokenValidationParameters.RoleClaimType = options.RoleClaim;
                    jwt.TokenValidationParameters.NameClaimType = "name";
                });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicies.Platform, policy => policy.RequireRole(AdminRoles.PlatformAdmin))
            .AddPolicy(AdminPolicies.Tenant, policy => policy
                .RequireRole(AdminRoles.TenantAdmin)
                .RequireClaim(AdminClaims.Tenant)
                .AddRequirements(new ActiveTenantRequirement()));
        services.AddScoped<IAuthorizationHandler, ActiveTenantHandler>();

        return services;
    }
}
