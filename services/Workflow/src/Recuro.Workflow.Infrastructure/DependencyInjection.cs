using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Infrastructure.Clients;
using Recuro.Workflow.Infrastructure.Persistence;
using Recuro.Workflow.Infrastructure.Scheduling;

namespace Recuro.Workflow.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "workflow";

    /// <summary>
    /// Persistence, messaging and the escalation scheduler. The typed <see cref="ConfigCalendarClient"/> is
    /// registered by the composition root, which adds the identity-forwarding handler from the web layer.
    /// </summary>
    public static IServiceCollection AddWorkflowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<WorkflowDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddOptions<ServiceEndpoints>().BindConfiguration(ServiceEndpoints.SectionName);
        services.AddOptions<EscalationOptions>().BindConfiguration(EscalationOptions.SectionName);
        services.AddSingleton<EscalationScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<EscalationScheduler>());

        // Workflow only publishes for now; it consumes nothing.
        services.AddRecuroMessaging(configuration);
        return services;
    }
}
