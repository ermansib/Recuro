using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Application.Applications;
using Recuro.Pipeline.Application.Applications.Events;
using Recuro.Pipeline.Infrastructure.Integration;
using Recuro.Pipeline.Infrastructure.Jobs;
using Recuro.Pipeline.Infrastructure.Persistence;

namespace Recuro.Pipeline.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "pipeline";

    public static IServiceCollection AddPipelineInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PipelineOptions>().BindConfiguration(PipelineOptions.SectionName);
        services.AddRecuroPersistence<PipelineDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<ISourcingGateRepository, SourcingGateRepository>();
        services.AddScoped<IApplicationNumbers, ApplicationNumbers>();
        services.AddScoped<ProgressEventsHandler>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<SourcingUnlockedPayload, RequisitionEventsHandler>(EventTypes.Requisition.SourcingUnlocked)
            .Subscribe<RequisitionCancelledPayload, RequisitionEventsHandler>(EventTypes.Requisition.Cancelled)
            .Subscribe<ApplicationRefPayload, SelectionRatifiedHandler>(EventTypes.Interview.SelectionRatified)
            .Subscribe<ApplicationRefPayload, BgvClearedHandler>(EventTypes.Bgv.Cleared)
            .Subscribe<ApplicationRefPayload, BgvAdverseFlaggedHandler>(EventTypes.Bgv.AdverseFlagged)
            .Subscribe<ApplicationRefPayload, OfferAcceptedHandler>(EventTypes.Offer.Accepted);

        services.AddOptions<TatScanOptions>().BindConfiguration(TatScanOptions.SectionName);
        services.AddHostedService<TatScanJob>();
        return services;
    }

    /// <summary>
    /// The typed client for the Candidate service. The API adds handlers that forward the caller's
    /// credentials and correlation ids.
    /// </summary>
    public static IHttpClientBuilder AddCandidateDirectory(this IServiceCollection services)
    {
        services.AddOptions<CandidateServiceOptions>().BindConfiguration(CandidateServiceOptions.SectionName);
        return services.AddHttpClient<ICandidateDirectory, CandidateDirectoryClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<CandidateServiceOptions>>().Value;
            http.BaseAddress = options.BaseUrl;
            http.Timeout = options.Timeout;
        });
    }
}
