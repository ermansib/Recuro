using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Application.Events;
using Recuro.Offer.Application.Offers.Commands;
using Recuro.Offer.Infrastructure.Adapters;
using Recuro.Offer.Infrastructure.Clients;
using Recuro.Offer.Infrastructure.Jobs;
using Recuro.Offer.Infrastructure.Persistence;

namespace Recuro.Offer.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "offer";

    /// <summary>
    /// Persistence, messaging, letters, e-sign and the lifecycle job. The typed HTTP clients are registered
    /// by the composition root (see <see cref="ServiceEndpoints.Apply"/>), which adds the identity-forwarding
    /// handler from the web layer.
    /// </summary>
    public static IServiceCollection AddOfferInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<OfferDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IApplicationTrackRepository, ApplicationTrackRepository>();
        services.AddScoped<IBgvTrackRepository, BgvTrackRepository>();
        services.AddScoped<IOfferDocumentRepository, OfferDocumentRepository>();
        services.AddMemoryCache();
        services.AddOptions<ServiceEndpoints>().BindConfiguration(ServiceEndpoints.SectionName);

        services.AddOptions<LetterOptions>().BindConfiguration(LetterOptions.SectionName);
        services.AddSingleton<ILetterRenderer, PdfLetterRenderer>();
        services.AddSingleton<ILetterLinks, HmacLetterLinks>();
        services.AddSingleton<IESignGateway, LoggingESignGateway>();
        services.AddSingleton<ESignCallbackVerifier>();

        services.AddOptions<OfferLifecycleOptions>().BindConfiguration(OfferLifecycleOptions.SectionName);
        services.AddHostedService<OfferLifecycleJob>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<WorkflowTaskCompletedPayload, OfferApprovalCompletedHandler>(EventTypes.Workflow.TaskCompleted)
            .Subscribe<StageChangedPayload, PipelineStageChangedHandler>(EventTypes.Pipeline.StageChanged)
            .Subscribe<BgvEventPayload, BgvGateHandler>(EventTypes.Bgv.CaseInitiated)
            .Subscribe<BgvEventPayload, BgvGateHandler>(EventTypes.Bgv.Cleared)
            .Subscribe<BgvEventPayload, BgvGateHandler>(EventTypes.Bgv.AdverseFlagged)
            .Subscribe<BgvEventPayload, BgvGateHandler>(EventTypes.Bgv.Resolved)
            .Subscribe<CandidatePurgedPayload, CandidatePurgedHandler>(EventTypes.Candidate.Purged);
        return services;
    }
}
