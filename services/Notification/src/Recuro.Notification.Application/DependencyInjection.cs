using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Notification.Application.Events;

namespace Recuro.Notification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly);
        services.AddScoped<RecipientResolver>();
        return services;
    }
}
