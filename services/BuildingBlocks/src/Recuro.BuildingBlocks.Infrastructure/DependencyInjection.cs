using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Outbox;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The service's own PostgreSQL database (database-per-service): DbContext, unit of work, outbox
    /// publisher, domain-event dispatch and a readiness check.
    /// </summary>
    public static IServiceCollection AddRecuroPersistence<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName)
        where TContext : RecuroDbContext
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{connectionStringName} is not set.");
        var migrationsAssembly = typeof(TContext).Assembly.GetName().Name!;

        services.AddDbContext<TContext>(options => RecuroNpgsql.Configure(options, connectionString, migrationsAssembly));
        services.AddScoped<RecuroDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.AddOptions<ServiceIdentity>().BindConfiguration(ServiceIdentity.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHealthChecks().AddDbContextCheck<TContext>("database", tags: [HealthTags.Ready]);
        return services;
    }

    /// <summary>
    /// RabbitMQ transport: the outbox relay, the service's consumer queue and its subscriptions.
    /// With <c>Messaging:Enabled=false</c> only the in-process processor is registered (tests).
    /// </summary>
    public static MessagingBuilder AddRecuroMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MessagingOptions.SectionName);
        services.AddOptions<MessagingOptions>()
            .Bind(section)
            .PostConfigure(o => o.ConnectionString ??= configuration.GetConnectionString("rabbitmq"));
        services.AddOptions<ServiceIdentity>()
            .BindConfiguration(ServiceIdentity.SectionName)
            .Validate(s => !string.IsNullOrWhiteSpace(s.Name), "Service:Name must be set (it names the queue).")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        var subscriptions = new EventSubscriptions();
        services.AddSingleton(subscriptions);
        services.AddSingleton<IntegrationEventProcessor>();

        if (section.GetValue(nameof(MessagingOptions.Enabled), defaultValue: true))
        {
            services.AddSingleton<RabbitMqConnection>();
            services.AddSingleton<IMessageBroker, RabbitMqBroker>();
            services.AddHostedService<OutboxRelay>();
            services.AddHostedService<RabbitMqConsumer>();
            services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [HealthTags.Ready]);
        }

        return new MessagingBuilder(services, subscriptions);
    }
}

/// <summary>Adds event subscriptions for this service's consumer queue.</summary>
public sealed class MessagingBuilder(IServiceCollection services, EventSubscriptions subscriptions)
{
    /// <summary>
    /// Runs <typeparamref name="THandler"/> for every event of <paramref name="eventType"/>
    /// (a name from <see cref="EventTypes"/>, or <see cref="EventTypes.All"/>).
    /// </summary>
    public MessagingBuilder Subscribe<TData, THandler>(string eventType)
        where THandler : class, IIntegrationEventHandler<TData>
    {
        services.TryAddScoped<THandler>();
        subscriptions.Add<TData, THandler>(eventType);
        return this;
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.GetAsync(cancellationToken);
            return connection.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", ex);
        }
    }
}
