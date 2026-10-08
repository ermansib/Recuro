using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;

namespace Recuro.Vendor.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddVendorApplication(this IServiceCollection services) =>
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
}
