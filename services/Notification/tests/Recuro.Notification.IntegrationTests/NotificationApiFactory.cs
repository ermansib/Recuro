using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Recuro.Notification.IntegrationTests;

/// <summary>The real API on a real PostgreSQL (Testcontainers), signed in with X-Dev-* headers. Email goes to a fake transport.</summary>
public sealed class NotificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantA = Guid.Parse("7b2e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("7b2e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeEmailTransport Mail { get; } = new();

    /// <summary>Candidate and Identity, as Notification calls them (RCU-AUT-005).</summary>
    public FakeServices Backends { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient ClientFor(Guid tenant, string role, string user = "u-1")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.RolesHeader, role);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.TenantHeader, tenant.ToString());
        return client;
    }

    /// <summary>Delivers an event as the RabbitMQ consumer would (inbox, tenant scope, transaction).</summary>
    public Task PublishAsync(
        string type,
        object data,
        Guid? tenant = null,
        string subject = "Entity/1",
        string? actorId = "actor-1",
        string? actorName = "A. Actor",
        Guid? id = null) =>
        Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(
            new CloudEvent
            {
                Id = id ?? Guid.CreateVersion7(),
                Source = "/services/test",
                Type = type,
                Subject = subject,
                Time = DateTimeOffset.UtcNow,
                TenantId = tenant ?? TenantA,
                ActorId = actorId,
                ActorName = actorName,
                Data = JsonSerializer.SerializeToElement(data),
            },
            CancellationToken.None);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:notification", _postgres.GetConnectionString());
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("EmailDispatch:Enabled", "false");
        builder.UseSetting("Email:RetryBackoff", "00:00:00");
        builder.UseSetting("NotificationStream:HeartbeatInterval", "00:00:01");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Services:Candidate:BaseUrl", "http://candidate.test/");
        builder.UseSetting("Services:Identity:BaseUrl", "http://identity.test/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailTransport>(Mail);
            foreach (var client in new[] { nameof(ICandidateContacts), nameof(IStaffDirectory) })
            {
                services.Configure<HttpClientFactoryOptions>(client, o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Backends));
            }
        });
    }
}

/// <summary>Records sent mail; can be told to fail for an address.</summary>
public sealed class FakeEmailTransport : IEmailTransport
{
    public ConcurrentBag<EmailMessage> Sent { get; } = [];

    public ConcurrentDictionary<string, bool> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (message.ToAddress is { } to && FailFor.ContainsKey(to))
        {
            throw new InvalidOperationException("SMTP 451 temporary failure");
        }

        Sent.Add(message);
        return Task.FromResult($"<{Guid.NewGuid():N}@test>");
    }
}

/// <summary>
/// Fake Candidate and Identity. Candidate knows the ids in <see cref="Candidates"/> (404 otherwise);
/// Identity lists the people in <see cref="Staff"/> for a tenant and role, and answers 503 for anything
/// else, so Notification falls back to its local directory.
/// </summary>
public sealed class FakeServices : HttpMessageHandler
{
    public ConcurrentDictionary<Guid, (string Name, string Email)> Candidates { get; } = new();

    public ConcurrentDictionary<(Guid Tenant, string Role), StaffContact[]> Staff { get; } = new();

    public ConcurrentBag<(string Path, string? Roles, string? Tenant)> Seen { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var roles = Header(request, DevelopmentAuthenticationHandler.RolesHeader);
        var tenant = Header(request, DevelopmentAuthenticationHandler.TenantHeader);
        Seen.Add((path, roles, tenant));
        if (roles != RecuroRoles.Service || !Guid.TryParse(tenant, out var tenantId))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        if (request.RequestUri.Host == "candidate.test"
            && Guid.TryParse(path[(path.LastIndexOf('/') + 1)..], out var id)
            && Candidates.TryGetValue(id, out var candidate))
        {
            return Json(new { id, name = candidate.Name, email = candidate.Email, phone = "**********" });
        }

        if (request.RequestUri.Host == "identity.test"
            && System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["role"] is { } role
            && Staff.TryGetValue((tenantId, role), out var people))
        {
            return Json(people.Select(p => new { id = p.UserId, tenantId, name = p.Name, role, email = p.Email }));
        }

        return Task.FromResult(new HttpResponseMessage(request.RequestUri.Host == "identity.test" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NotFound));
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static Task<HttpResponseMessage> Json(object body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) });
}
