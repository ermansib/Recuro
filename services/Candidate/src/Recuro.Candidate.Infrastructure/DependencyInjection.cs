using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Application.Candidates.Events;
using Recuro.Candidate.Infrastructure.Integration;
using Recuro.Candidate.Infrastructure.Jobs;
using Recuro.Candidate.Infrastructure.Persistence;
using Recuro.Candidate.Infrastructure.Pii;
using Recuro.Candidate.Infrastructure.Storage;

namespace Recuro.Candidate.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "candidate";

    public static IServiceCollection AddCandidateInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PiiOptions>()
            .BindConfiguration(PiiOptions.SectionName)
            .Validate(o => o.Keys.Count > 0 && o.Keys.Values.All(PiiOptions.IsKey), "Pii:Keys must hold base64 32-byte keys.")
            .Validate(o => o.Keys.ContainsKey(o.ActiveKeyId), "Pii:ActiveKeyId must name one of Pii:Keys.")
            .Validate(o => PiiOptions.IsKey(o.BlindIndexKey), "Pii:BlindIndexKey must be a base64 32-byte key.")
            .ValidateOnStart();
        services.AddSingleton<IPiiCipher, AesGcmPiiCipher>();
        services.AddSingleton<IContactFingerprinter, ContactFingerprinter>();
        services.AddSingleton<IMaskHasher, KeyedMaskHasher>();

        services.AddRecuroPersistence<CandidateDbContext>(configuration, ConnectionStringName);
        services.AddScoped<ICandidateRepository, CandidateRepository>();

        services.AddOptions<ResumeStorageOptions>().BindConfiguration(ResumeStorageOptions.SectionName);
        services.AddSingleton<IResumeStore, LocalResumeStore>();
        services.AddScoped<IPersonalDataAccessLog, LoggingPersonalDataAccessLog>();

        services.AddOptions<RetentionOptions>().BindConfiguration(RetentionOptions.SectionName);
        services.AddRecuroMessaging(configuration)
            .Subscribe<ApplicationCreatedPayload, ApplicationLifecycleHandler>(EventTypes.Pipeline.ApplicationCreated)
            .Subscribe<ApplicationFinalRejectedPayload, ApplicationLifecycleHandler>(EventTypes.Pipeline.FinalRejected)
            .Subscribe<ApplicationStageChangedPayload, ApplicationLifecycleHandler>(EventTypes.Pipeline.StageChanged);

        services.AddOptions<RetentionPurgeOptions>().BindConfiguration(RetentionPurgeOptions.SectionName);
        services.AddHostedService<RetentionPurgeJob>();
        return services;
    }

    /// <summary>
    /// The typed client for Identity's masking maps (RCU-AUT-004). The API adds handlers that forward the
    /// caller's credentials and correlation ids.
    /// </summary>
    public static IHttpClientBuilder AddIdentityMaskingMaps(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddOptions<IdentityServiceOptions>().BindConfiguration(IdentityServiceOptions.SectionName);
        return services.AddHttpClient<IMaskingMaps, IdentityMaskingMaps>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value;
            http.BaseAddress = options.BaseUrl;
            http.Timeout = options.Timeout;
        });
    }

    /// <summary>
    /// The consultant check (RCU-CND-005): Vendor's status endpoint when <c>Services:Vendor:Enabled</c> is
    /// on, otherwise the unchecked stand-in. Returns the HTTP client builder so the API can add handlers.
    /// </summary>
    public static IHttpClientBuilder AddVendorDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VendorServiceOptions>().BindConfiguration(VendorServiceOptions.SectionName);
        var enabled = configuration.GetSection(VendorServiceOptions.SectionName).GetValue<bool>(nameof(VendorServiceOptions.Enabled));
        if (!enabled)
        {
            services.AddSingleton<IVendorDirectory, UncheckedVendorDirectory>();
            return services.AddHttpClient(nameof(VendorDirectoryClient));
        }

        return services.AddHttpClient<IVendorDirectory, VendorDirectoryClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<VendorServiceOptions>>().Value;
            http.BaseAddress = options.BaseUrl;
            http.Timeout = options.Timeout;
        });
    }
}
