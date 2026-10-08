using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Application.Cases;
using Recuro.Bgv.Application.Cases.Events;
using Recuro.Bgv.Infrastructure.Integration;
using Recuro.Bgv.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;

namespace Recuro.Bgv.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "bgv";

    public static IServiceCollection AddBgvInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BgvOptions>().BindConfiguration(BgvOptions.SectionName);
        services.AddRecuroPersistence<BgvDbContext>(configuration, ConnectionStringName);
        services.AddScoped<IBgvCaseRepository, BgvCaseRepository>();
        services.AddScoped<IBgvRequestRepository, BgvRequestRepository>();

        services.AddRecuroMessaging(configuration)
            .Subscribe<StageChangedPayload, StageChangedHandler>(EventTypes.Pipeline.StageChanged)
            .Subscribe<VendorDeEmpanelledPayload, VendorDeEmpanelledHandler>(EventTypes.Vendor.DeEmpanelled)
            .Subscribe<WorkflowTaskCompletedPayload, AdverseDecisionHandler>(EventTypes.Workflow.TaskCompleted);
        return services;
    }

    // The typed HTTP clients below are completed by the API, which adds the handlers that forward the
    // caller's credentials and correlation ids (zero trust: the callee authorises the real caller).

    public static IHttpClientBuilder AddConfigRules(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddOptions<ConfigServiceOptions>().BindConfiguration(ConfigServiceOptions.SectionName);
        return services.AddHttpClient<IBgvRules, ConfigRulesClient>((sp, http) => Apply(http, sp.GetRequiredService<IOptions<ConfigServiceOptions>>().Value.BaseUrl, sp.GetRequiredService<IOptions<ConfigServiceOptions>>().Value.Timeout));
    }

    public static IHttpClientBuilder AddVendorDirectory(this IServiceCollection services)
    {
        services.AddOptions<VendorServiceOptions>().BindConfiguration(VendorServiceOptions.SectionName);
        return services.AddHttpClient<IVendorDirectory, VendorDirectoryClient>((sp, http) => Apply(http, sp.GetRequiredService<IOptions<VendorServiceOptions>>().Value.BaseUrl, sp.GetRequiredService<IOptions<VendorServiceOptions>>().Value.Timeout));
    }

    public static IHttpClientBuilder AddWorkflowClient(this IServiceCollection services)
    {
        services.AddOptions<WorkflowServiceOptions>().BindConfiguration(WorkflowServiceOptions.SectionName);
        return services.AddHttpClient<IWorkflowClient, WorkflowHttpClient>((sp, http) => Apply(http, sp.GetRequiredService<IOptions<WorkflowServiceOptions>>().Value.BaseUrl, sp.GetRequiredService<IOptions<WorkflowServiceOptions>>().Value.Timeout));
    }

    public static IHttpClientBuilder AddSensitiveNotePolicy(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddOptions<IdentityServiceOptions>().BindConfiguration(IdentityServiceOptions.SectionName);
        return services.AddHttpClient<ISensitiveNotePolicy, IdentityMaskingClient>((sp, http) => Apply(http, sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value.BaseUrl, sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value.Timeout));
    }

    private static void Apply(HttpClient http, Uri baseUrl, TimeSpan timeout)
    {
        http.BaseAddress = baseUrl;
        http.Timeout = timeout;
    }
}
