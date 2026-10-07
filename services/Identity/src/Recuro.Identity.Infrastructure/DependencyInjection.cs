using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Identity.Application.Abstractions;
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
        services.AddSingleton<IAccessPolicyProvider, DefaultPolicyProvider>();
        services.AddSingleton<IMaskingMapProvider, DefaultPolicyProvider>();

        // Identity publishes but consumes nothing yet; this registers the outbox relay.
        services.AddRecuroMessaging(configuration);
        return services;
    }
}
