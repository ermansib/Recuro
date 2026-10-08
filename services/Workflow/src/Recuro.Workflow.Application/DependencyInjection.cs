using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Workflow.Application.Workflows;

namespace Recuro.Workflow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkflowApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<SlaPlanner>();
        return services;
    }
}
