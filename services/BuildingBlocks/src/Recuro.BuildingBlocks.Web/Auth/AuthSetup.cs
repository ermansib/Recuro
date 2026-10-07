using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Recuro.BuildingBlocks.Web.Auth;

public static class AuthSetup
{
    /// <summary>
    /// Every endpoint requires a signed-in caller with a tenant unless it says <c>AllowAnonymous</c>
    /// (fail closed). Services validate the token themselves even behind the gateway (zero trust).
    /// </summary>
    public static IServiceCollection AddRecuroAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(RecuroAuthOptions.SectionName).Get<RecuroAuthOptions>() ?? new RecuroAuthOptions();

        if (options.IsDevelopmentMode)
        {
            if (!(environment.IsDevelopment() || environment.IsEnvironment("Testing")))
            {
                throw new InvalidOperationException("Auth:Mode=Development is only allowed in the Development or Testing environment.");
            }

            services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.Authority))
            {
                throw new InvalidOperationException("Auth:Authority must be the Keycloak realm URL, e.g. http://localhost:8080/realms/recuro.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt =>
                {
                    jwt.Authority = options.Authority;
                    jwt.Audience = options.Audience;
                    if (!string.IsNullOrWhiteSpace(options.MetadataAddress))
                    {
                        jwt.MetadataAddress = options.MetadataAddress;
                    }

                    jwt.MapInboundClaims = false;
                    // Keycloak runs on plain http locally; everywhere else discovery must be https.
                    jwt.RequireHttpsMetadata = !environment.IsDevelopment();
                    jwt.TokenValidationParameters.ValidIssuer = options.Authority;
                    jwt.TokenValidationParameters.RoleClaimType = RecuroClaims.Roles;
                    jwt.TokenValidationParameters.NameClaimType = RecuroClaims.Name;
                });
        }

        var tenantMember = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(RecuroClaims.Tenant)
            .Build();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(tenantMember)
            .SetFallbackPolicy(tenantMember);

        return services;
    }

    /// <summary>A policy: a tenant member holding one of <paramref name="roles"/>.</summary>
    public static AuthorizationBuilder AddRolePolicy(this AuthorizationBuilder builder, string name, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddPolicy(name, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(RecuroClaims.Tenant)
            .RequireRole(roles));
    }
}
