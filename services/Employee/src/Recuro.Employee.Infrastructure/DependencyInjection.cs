using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Employee.Application.Abstractions;
using Recuro.Employee.Application.Events;
using Recuro.Employee.Infrastructure.Clients;
using Recuro.Employee.Infrastructure.Persistence;

namespace Recuro.Employee.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "employee";

    public static IServiceCollection AddEmployeeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<EmployeeDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IIjpPostingRepository, IjpPostingRepository>();
        services.AddScoped<IIntakeRepository, IntakeRepository>();
        services.AddScoped<ISourcingGateRepository, SourcingGateRepository>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<SourcingUnlockedPayload, RequisitionEventsHandler>(EventTypes.Requisition.SourcingUnlocked)
            .Subscribe<RequisitionCancelledPayload, RequisitionEventsHandler>(EventTypes.Requisition.Cancelled)
            .Subscribe<StageChangedPayload, IntakeProgressHandler>(EventTypes.Pipeline.StageChanged)
            .Subscribe<FinalRejectedPayload, IntakeProgressHandler>(EventTypes.Pipeline.FinalRejected);
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
