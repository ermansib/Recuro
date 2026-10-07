using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Infrastructure.Persistence;

namespace Recuro.Config.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "config";

    public static IServiceCollection AddConfigInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<ConfigDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IRuleSetVersions, RuleSetVersions>();

        // Config publishes config.version.activated.v1 and consumes nothing; this registers the outbox relay.
        services.AddRecuroMessaging(configuration);
        return services;
    }
}
