using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Application.Requisitions.Events;
using Recuro.Requisition.Infrastructure.Clients;
using Recuro.Requisition.Infrastructure.Persistence;

namespace Recuro.Requisition.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "requisition";

    /// <summary>
    /// Persistence and messaging. The typed HTTP clients (<see cref="ConfigRulesClient"/>,
    /// <see cref="WorkflowHttpClient"/>) are registered by the composition root (see <see cref="ServiceEndpoints.Apply"/>), which adds the
    /// identity-forwarding handler from the web layer.
    /// </summary>
    public static IServiceCollection AddRequisitionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<RequisitionDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IRequisitionRepository, RequisitionRepository>();
        services.AddScoped<IJobDescriptionRepository, JobDescriptionRepository>();
        services.AddScoped<IReqIdAllocator, ReqIdAllocator>();
        services.AddOptions<ServiceEndpoints>().BindConfiguration(ServiceEndpoints.SectionName);

        services.AddRecuroMessaging(configuration)
            .Subscribe<WorkflowTaskCompletedPayload, WorkflowTaskCompletedHandler>(EventTypes.Workflow.TaskCompleted);
        return services;
    }
}
