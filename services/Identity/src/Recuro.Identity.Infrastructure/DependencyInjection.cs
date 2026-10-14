using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Infrastructure.Integration;
using Recuro.Identity.Infrastructure.Persistence;
using Recuro.Identity.Infrastructure.Policies;

namespace Recuro.Identity.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "identity";

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<IdentityDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<WorkspacePolicyProvider>();
        services.AddScoped<IAccessPolicyProvider>(sp => sp.GetRequiredService<WorkspacePolicyProvider>());
        services.AddScoped<IMaskingMapProvider>(sp => sp.GetRequiredService<WorkspacePolicyProvider>());

        // Identity publishes but consumes nothing yet; this registers the outbox relay.
        services.AddRecuroMessaging(configuration);
        return services;
    }

    /// <summary>
    /// Workspace roles from the admin API. The API completes the client with the service-account token handler,
    /// so the call is made as this service whatever the caller.
    /// </summary>
    public static IHttpClientBuilder AddWorkspaceAccess(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddOptions<AdminServiceOptions>().BindConfiguration(AdminServiceOptions.SectionName);
        return services.AddHttpClient<IWorkspaceAccessSource, AdminWorkspaceAccessClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<AdminServiceOptions>>().Value;
            http.BaseAddress = options.BaseUrl;
            http.Timeout = options.Timeout;
        });
    }
}
