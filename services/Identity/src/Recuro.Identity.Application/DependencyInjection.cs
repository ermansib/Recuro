using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;

namespace Recuro.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services) =>
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
}
