using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Requisition.Application.Requisitions;

namespace Recuro.Requisition.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddRequisitionApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<RequisitionSubmission>();
        return services;
    }
}
