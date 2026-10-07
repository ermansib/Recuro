using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Recuro.Admin.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use-case handler (classes named *Handler) as scoped.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlers = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal));

        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }

        return services;
    }
}
