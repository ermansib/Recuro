using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Interview.Application.Interviews.Commands;

namespace Recuro.Interview.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInterviewApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<AssessmentWriter>();
        return services;
    }
}
