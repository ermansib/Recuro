using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Onboarding.Application.Cases;

namespace Recuro.Onboarding.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOnboardingApplication(this IServiceCollection services)
    {
        services.AddScoped<CaseViews>();
        return services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
    }
}
