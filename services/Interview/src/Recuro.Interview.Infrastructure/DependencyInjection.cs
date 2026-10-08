using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Application.Events;
using Recuro.Interview.Application.Selection;
using Recuro.Interview.Infrastructure.Adapters;
using Recuro.Interview.Infrastructure.Clients;
using Recuro.Interview.Infrastructure.Jobs;
using Recuro.Interview.Infrastructure.Persistence;

namespace Recuro.Interview.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "interview";

    /// <summary>
    /// Persistence, messaging, adapters and the SLA job. The typed HTTP clients are registered by the
    /// composition root (see <see cref="ServiceEndpoints.Apply"/>), which adds the identity-forwarding
    /// handler from the web layer.
    /// </summary>
    public static IServiceCollection AddInterviewInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<InterviewDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IInterviewRepository, InterviewRepository>();
        services.AddScoped<IApplicationTrackRepository, ApplicationTrackRepository>();
        services.AddScoped<ISelectionRepository, SelectionRepository>();
        services.AddSingleton<ICalendarInvites, LoggingCalendarInvites>();
        services.AddSingleton<IPdfRenderer, SimplePdfRenderer>();
        services.AddMemoryCache();
        services.AddOptions<ServiceEndpoints>().BindConfiguration(ServiceEndpoints.SectionName);

        services.AddOptions<FeedbackSlaOptions>().BindConfiguration(FeedbackSlaOptions.SectionName);
        services.AddHostedService<FeedbackSlaJob>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<StageChangedPayload, PipelineStageChangedHandler>(EventTypes.Pipeline.StageChanged)
            .Subscribe<WorkflowTaskCompletedPayload, RatificationCompletedHandler>(EventTypes.Workflow.TaskCompleted);
        return services;
    }
}
