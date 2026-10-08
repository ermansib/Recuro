using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Offer.Application.Offers;

namespace Recuro.Offer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOfferApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<OfferWriter>();
        services.AddScoped<ICallerMask, CallerMask>();
        return services;
    }
}
