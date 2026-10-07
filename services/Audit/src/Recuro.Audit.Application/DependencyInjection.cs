using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;

namespace Recuro.Audit.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAuditApplication(this IServiceCollection services) =>
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
}
