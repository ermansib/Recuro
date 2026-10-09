using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Application.Events;
using Recuro.Reporting.Application.Projections;
using Recuro.Reporting.Infrastructure.Clients;
using Recuro.Reporting.Infrastructure.Jobs;
using Recuro.Reporting.Infrastructure.Packs;
using Recuro.Reporting.Infrastructure.Persistence;

namespace Recuro.Reporting.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "reporting";

    /// <summary>
    /// Persistence, the event consumer, pack rendering and the schedule. The typed HTTP clients (Config,
    /// Identity) are registered by the composition root (see <see cref="ServiceEndpoints.Apply"/>), which
    /// adds the identity-forwarding and service-token handlers from the web layer.
    /// </summary>
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<ReportingDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IEventLog, EventLog>();
        services.AddScoped<IProjectionStore, ProjectionStore>();
        services.AddScoped<ICostLedger, CostLedger>();
        services.AddScoped<IMetricDefinitionStore, MetricDefinitionStore>();
        services.AddScoped<IReportSettingsStore, ReportSettingsStore>();
        services.AddScoped<ISnapshotStore, SnapshotStore>();
        services.AddScoped<IPackArchive, PackArchive>();
        services.AddSingleton<IPackRenderer, PackRenderer>();
        services.AddMemoryCache();
        services.AddOptions<ServiceEndpoints>().BindConfiguration(ServiceEndpoints.SectionName);
        services.AddOptions<ReportingOptions>().BindConfiguration(ReportingOptions.SectionName);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ReportingOptions>>().Value);

        services.AddOptions<ReportScheduleOptions>().BindConfiguration(ReportScheduleOptions.SectionName);
        services.AddHostedService<ReportScheduleJob>();

        var messaging = services.AddRecuroMessaging(configuration);
        foreach (var type in ReportProjector.EventTypes)
        {
            messaging.Subscribe<JsonElement, ReportEventHandler>(type);
        }

        messaging
            .Subscribe<EmailDispatchedPayload, PackDeliveryHandler>(EventTypes.Notification.EmailDispatched)
            .Subscribe<EmailDispatchedPayload, PackDeliveryHandler>(EventTypes.Notification.EmailFailed);
        return services;
    }
}
