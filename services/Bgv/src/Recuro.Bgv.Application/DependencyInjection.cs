using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;

namespace Recuro.Bgv.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBgvApplication(this IServiceCollection services) =>
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
}
