using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddRecuroPersistence<CandidateDbContext>(configuration, ConnectionStringName);
        services.AddScoped<ICandidateRepository, CandidateRepository>();

        services.AddOptions<ResumeStorageOptions>().BindConfiguration(ResumeStorageOptions.SectionName);
        services.AddSingleton<IResumeStore, LocalResumeStore>();
        services.AddSingleton<IVendorDirectory, UncheckedVendorDirectory>();
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
}
