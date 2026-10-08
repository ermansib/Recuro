using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Vendor.Application.Abstractions;
using Recuro.Vendor.Application.Vendors;
using Recuro.Vendor.Infrastructure.Persistence;

namespace Recuro.Vendor.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "vendor";

    /// <summary>
    /// Persistence and messaging. Vendor publishes <c>vendor.*</c>; the <c>bgv.check.updated</c> SLA
    /// roll-up (RCU-VND-004, P1) subscribes here when it is built.
    /// </summary>
    public static IServiceCollection AddVendorInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VendorOptions>().BindConfiguration(VendorOptions.SectionName);
        services.AddRecuroPersistence<VendorDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IVendorRepository, VendorRepository>();
        services.AddRecuroMessaging(configuration);
        return services;
    }
}
