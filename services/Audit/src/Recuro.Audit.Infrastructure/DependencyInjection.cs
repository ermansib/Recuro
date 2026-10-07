using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.Audit.Application.Abstractions;
using Recuro.Audit.Application.Entries.Events;
using Recuro.Audit.Infrastructure.Jobs;
using Recuro.Audit.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;

namespace Recuro.Audit.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "audit";

    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<AuditDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IAuditChainWriter, AuditChainWriter>();
        services.AddScoped<IAuditReadStore, AuditReadStore>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<JsonElement, AuditMirrorHandler>(EventTypes.All);

        services.AddOptions<AuditSealOptions>().BindConfiguration(AuditSealOptions.SectionName);
        services.AddHostedService<AuditSealJob>();
        return services;
    }
}
