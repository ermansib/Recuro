using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Application.Events;
using Recuro.Careers.Infrastructure.Clients;
using Recuro.Careers.Infrastructure.Persistence;

namespace Recuro.Careers.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "careers";

    public static IServiceCollection AddCareersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<CareersDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IPostingRepository, PostingRepository>();
        services.AddScoped<ISourcingGateRepository, SourcingGateRepository>();
        services.AddScoped<IPublicApplicationRepository, PublicApplicationRepository>();
        services.AddScoped<IJobSearchCache, JobSearchCache>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<SourcingUnlockedPayload, RequisitionEventsHandler>(EventTypes.Requisition.SourcingUnlocked)
            .Subscribe<RequisitionCancelledPayload, RequisitionEventsHandler>(EventTypes.Requisition.Cancelled)
            .Subscribe<StageChangedPayload, ApplicationProgressHandler>(EventTypes.Pipeline.StageChanged)
            .Subscribe<FinalRejectedPayload, ApplicationProgressHandler>(EventTypes.Pipeline.FinalRejected)
            .Subscribe<EmailDispatchedPayload, ApplicationProgressHandler>(EventTypes.Notification.EmailDispatched);
        return services;
    }

    /// <summary>The Candidate service, for the intake saga. The API adds the handlers that sign calls as this service.</summary>
    public static IHttpClientBuilder AddCandidateIntake(this IServiceCollection services) =>
        services.AddDownstream<ICandidateIntake, CandidateIntakeClient>("Candidate");

    /// <summary>The Pipeline service, for the intake saga. The API adds the handlers that sign calls as this service.</summary>
    public static IHttpClientBuilder AddPipelineIntake(this IServiceCollection services) =>
        services.AddDownstream<IPipelineIntake, PipelineIntakeClient>("Pipeline");

    /// <summary>The Config service's calendar. The API adds the handlers that forward the HR user's credentials.</summary>
    public static IHttpClientBuilder AddWorkingDayCalendar(this IServiceCollection services) =>
        services.AddDownstream<IWorkingDayCalendar, ConfigCalendarClient>("Config");

    private static IHttpClientBuilder AddDownstream<TClient, TImplementation>(this IServiceCollection services, string name)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddOptions<DownstreamOptions>(name).BindConfiguration($"Services:{name}");
        return services.AddHttpClient<TClient, TImplementation>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<DownstreamOptions>>().Get(name);
            http.BaseAddress = options.BaseUrl;
            http.Timeout = options.Timeout;
        });
    }
}
