using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Config.Application.RuleSets;

namespace Recuro.Config.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddConfigApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<RuleSetResolver>();
        return services;
    }
}
