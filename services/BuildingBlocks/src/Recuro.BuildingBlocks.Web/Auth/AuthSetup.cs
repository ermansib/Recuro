using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
                    jwt.Events = new JwtBearerEvents { OnTokenValidated = ServiceTenantHeader.ApplyAsync };
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

    /// <summary>
    /// RCU-AUT-005: this service's client-credentials token provider and <see cref="ServiceTokenHandler"/>,
    /// which a service adds to the HttpClients its event handlers and jobs use.
    /// </summary>
    public static IServiceCollection AddRecuroServiceTokens(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        services.AddOptions<RecuroAuthOptions>().BindConfiguration(RecuroAuthOptions.SectionName);
        services.AddOptions<ServiceAuthOptions>()
            .BindConfiguration(ServiceAuthOptions.SectionName)
            .PostConfigure<IOptions<RecuroAuthOptions>>((options, auth) =>
            {
                options.ClientId ??= $"recuro-svc-{serviceName}";
                if (environment.IsDevelopment())
                {
                    options.ClientSecret ??= $"{options.ClientId}-dev-secret";
                }

                options.TokenEndpoint ??= DefaultTokenEndpoint(auth.Value);
            });
        services.AddHttpClient(ServiceAuthOptions.HttpClientName);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IServiceTokenProvider, KeycloakServiceTokenProvider>();
        services.TryAddTransient<ServiceTokenHandler>();
        return services;
    }

    private static Uri? DefaultTokenEndpoint(RecuroAuthOptions auth)
    {
        const string Discovery = "/.well-known/openid-configuration";
        var realm = !string.IsNullOrWhiteSpace(auth.MetadataAddress) && auth.MetadataAddress.EndsWith(Discovery, StringComparison.Ordinal)
            ? auth.MetadataAddress[..^Discovery.Length]
            : auth.Authority;
        return string.IsNullOrWhiteSpace(realm) ? null : new Uri($"{realm.TrimEnd('/')}/protocol/openid-connect/token");
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
