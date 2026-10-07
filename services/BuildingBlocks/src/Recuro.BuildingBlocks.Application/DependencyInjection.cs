using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Behaviors;
using Recuro.BuildingBlocks.Application.Messaging;

namespace Recuro.BuildingBlocks.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the scope context, every handler and validator in <paramref name="applicationAssembly"/>,
    /// and wraps handlers in decorators. The last decorator registered runs first:
    /// logging → validation → handler.
    /// </summary>
    public static IServiceCollection AddRecuroApplication(this IServiceCollection services, Assembly applicationAssembly)
    {
        services.AddScoped<ScopeContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopeContext>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ScopeContext>());
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<ScopeContext>());

        services.Scan(scan => scan
            .FromAssemblies(applicationAssembly)
            .AddClasses(
                classes => classes.AssignableToAny(
                    typeof(ICommandHandler<>),
                    typeof(ICommandHandler<,>),
                    typeof(IQueryHandler<,>),
                    typeof(IDomainEventHandler<>)),
                publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.AddValidatorsFromAssembly(applicationAssembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationCommandDecorator<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationCommandDecorator<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(ValidationQueryDecorator<,>));

        services.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingCommandDecorator<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingCommandDecorator<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingQueryDecorator<,>));

        return services;
    }
}
