using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;

namespace Recuro.Careers.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCareersApplication(this IServiceCollection services)
    {
        services.AddOptions<CareersOptions>().BindConfiguration(CareersOptions.SectionName);
        return services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
    }
}
