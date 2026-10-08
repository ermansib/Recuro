using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;

namespace Recuro.Employee.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEmployeeApplication(this IServiceCollection services)
    {
        services.AddOptions<EmployeeOptions>().BindConfiguration(EmployeeOptions.SectionName);
        services.AddScoped<IntakeSaga>();
        return services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
    }
}
