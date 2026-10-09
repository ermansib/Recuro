using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Application.Events;
using Recuro.Onboarding.Infrastructure.Adapters;
using Recuro.Onboarding.Infrastructure.Clients;
using Recuro.Onboarding.Infrastructure.Jobs;
using Recuro.Onboarding.Infrastructure.Persistence;
using Recuro.Onboarding.Infrastructure.Storage;

namespace Recuro.Onboarding.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "onboarding";

    /// <summary>
    /// Persistence, messaging, document storage, the provisioning adapter and the milestone scheduler.
    /// The typed HTTP clients are registered by the composition root (see <see cref="ServiceEndpoints.Apply"/>),
    /// which adds the caller-forwarding and service-token handlers from the web layer.
    /// </summary>
    public static IServiceCollection AddOnboardingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<OnboardingDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IOnboardingCaseRepository, OnboardingCaseRepository>();
        services.AddScoped<IBgvTrackRepository, BgvTrackRepository>();
        services.AddMemoryCache();
        services.AddOptions<ServiceEndpoints>().BindConfiguration(ServiceEndpoints.SectionName);

        services.AddOptions<DocumentStorageOptions>().BindConfiguration(DocumentStorageOptions.SectionName);
        services.AddSingleton<IDocumentStore, LocalDocumentStore>();
        services.AddSingleton<IProvisioningAdapter, LoggingProvisioningAdapter>();
        services.AddSingleton<IConfirmationLetterRenderer, PdfConfirmationLetterRenderer>();

        services.AddOptions<MilestoneSchedulerOptions>().BindConfiguration(MilestoneSchedulerOptions.SectionName);
        services.AddSingleton<MilestoneSchedulerJob>();
        services.AddHostedService(sp => sp.GetRequiredService<MilestoneSchedulerJob>());

        services.AddRecuroMessaging(configuration)
            .Subscribe<OfferAcceptedPayload, OfferAcceptedHandler>(EventTypes.Offer.Accepted)
            .Subscribe<OfferWithdrawnPayload, OfferWithdrawnHandler>(EventTypes.Offer.Withdrawn)
            .Subscribe<BgvCaseInitiatedPayload, BgvStatusHandler>(EventTypes.Bgv.CaseInitiated)
            .Subscribe<BgvAdverseFlaggedPayload, BgvStatusHandler>(EventTypes.Bgv.AdverseFlagged)
            .Subscribe<BgvClearedPayload, BgvStatusHandler>(EventTypes.Bgv.Cleared)
            .Subscribe<BgvResolvedPayload, BgvStatusHandler>(EventTypes.Bgv.Resolved);
        return services;
    }

    /// <summary>Config (onboarding matrix, working days) and Candidate (letter name) as typed clients; the API adds the handlers.</summary>
    public static (IHttpClientBuilder Config, IHttpClientBuilder Candidate, IHttpClientBuilder Identity) AddOnboardingClients(this IServiceCollection services)
    {
        var config = services.AddHttpClient<ConfigClient>((sp, http) => Endpoints(sp).Apply(http, Endpoints(sp).Config, nameof(ServiceEndpoints.Config)));
        services.AddTransient<IOnboardingRules>(sp => sp.GetRequiredService<ConfigClient>());
        services.AddTransient<IWorkingDays>(sp => sp.GetRequiredService<ConfigClient>());
        var candidate = services.AddHttpClient<ICandidateDirectory, CandidateDirectoryClient>((sp, http) => Endpoints(sp).Apply(http, Endpoints(sp).Candidate, nameof(ServiceEndpoints.Candidate)));
        var identity = services.AddHttpClient<IPeopleDirectory, PeopleDirectoryClient>((sp, http) => Endpoints(sp).Apply(http, Endpoints(sp).Identity, nameof(ServiceEndpoints.Identity)));
        return (config, candidate, identity);
    }

    private static ServiceEndpoints Endpoints(IServiceProvider sp) =>
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpoints>>().Value;
}
