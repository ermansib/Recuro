using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailTransport>(Mail);
            services.AddSingleton<ICandidateContacts>(new KnownCandidates());
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

/// <summary>Candidate contacts for tests: <c>CND-*</c> ids have an address, others do not.</summary>
public sealed class KnownCandidates : ICandidateContacts
{
    public Task<CandidateContact?> FindAsync(string candidateId, CancellationToken ct) =>
        Task.FromResult(candidateId.StartsWith("CND-", StringComparison.Ordinal)
            ? new CandidateContact("A Candidate", $"{candidateId.ToLowerInvariant()}@example.test")
            : null);
}
