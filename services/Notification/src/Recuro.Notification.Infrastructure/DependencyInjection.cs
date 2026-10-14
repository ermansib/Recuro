using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Events;
using Recuro.Notification.Application.Templates;
using Recuro.Notification.Domain.Matrix;
using Recuro.Notification.Infrastructure.Caching;
using Recuro.Notification.Infrastructure.Directory;
using Recuro.Notification.Infrastructure.Email;
using Recuro.Notification.Infrastructure.Live;
using Recuro.Notification.Infrastructure.Persistence;

namespace Recuro.Notification.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "notification";

    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecuroPersistence<NotificationDbContext>(configuration, ConnectionStringName);
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<INotificationReadStore, NotificationReadStore>();
        services.AddScoped<IUnreadCountCache, DistributedUnreadCountCache>();
        services.AddSingleton<ITemplateSource, DefaultTemplates>();

        // Live updates: pg_notify inside the writing transaction, LISTEN on every replica.
        services.AddScoped<IFeedChangeSignal, PostgresFeedChangeSignal>();
        services.AddSingleton<FeedChangeListener>();
        services.AddSingleton<IFeedChangeListener>(sp => sp.GetRequiredService<FeedChangeListener>());
        services.AddHostedService<FeedChangeListenerService>();

        // Email: SMTP (Mailpit locally), queued in the database and sent by one replica at a time per row.
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value);
        services.AddOptions<SmtpOptions>().BindConfiguration(SmtpOptions.SectionName);
        services.AddOptions<EmailDispatchOptions>().BindConfiguration(EmailDispatchOptions.SectionName);
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();
        services.AddHostedService<EmailDispatchJob>();

        var messaging = services.AddRecuroMessaging(configuration);
        foreach (var eventType in NotificationMatrix.SubscribedEventTypes)
        {
            messaging.Subscribe<JsonElement, MatrixEventHandler>(eventType);
        }

        messaging
            .Subscribe<IdentityUserPayload, DirectoryEventHandler>(EventTypes.Identity.UserProvisioned)
            .Subscribe<IdentityUserPayload, DirectoryEventHandler>(EventTypes.Identity.RoleChanged);
        return services;
    }

    /// <summary>Candidate addresses for candidate emails. The Api adds the service-token handler (RCU-AUT-005).</summary>
    public static IHttpClientBuilder AddCandidateContacts(this IServiceCollection services) =>
        services.AddServiceClient<ICandidateContacts, CandidateContactsClient>(ServiceEndpointOptions.CandidateSection);

    /// <summary>Role members from Identity. The Api adds the service-token handler (RCU-AUT-005).</summary>
    public static IHttpClientBuilder AddStaffDirectory(this IServiceCollection services) =>
        services.AddServiceClient<IStaffDirectory, IdentityStaffDirectory>(ServiceEndpointOptions.IdentitySection);

    /// <summary>Requisition departments, for department-head recipients. The Api adds the service-token handler (RCU-AUT-005).</summary>
    public static IHttpClientBuilder AddRequisitionLookup(this IServiceCollection services) =>
        services.AddServiceClient<IRequisitionLookup, RequisitionLookupClient>(ServiceEndpointOptions.RequisitionSection);

    private static IHttpClientBuilder AddServiceClient<TClient, TImplementation>(this IServiceCollection services, string section)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddOptions<ServiceEndpointOptions>(section).BindConfiguration(section);
        return services.AddHttpClient<TClient, TImplementation>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<ServiceEndpointOptions>>().Get(section);
            http.BaseAddress = options.BaseUrl ?? throw new InvalidOperationException($"{section}:BaseUrl is required.");
            http.Timeout = options.Timeout;
        });
    }
}
