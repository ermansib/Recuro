using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Recuro.BuildingBlocks.IntegrationTests;

/// <summary>One PostgreSQL and one RabbitMQ container for the whole test run.</summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public string PostgresConnectionString(string database) =>
        new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public string RabbitConnectionString => _rabbit.GetConnectionString();

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "containers";
}

internal static class TestHost
{
    public static readonly Guid TenantA = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TenantB = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    /// <summary>A service's container, as its Program would build it, minus the web layer.</summary>
    public static ServiceProvider Build(ContainersFixture containers, string database, bool messaging, string serviceName = "widgets")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:test"] = containers.PostgresConnectionString(database),
                ["ConnectionStrings:rabbitmq"] = containers.RabbitConnectionString,
                ["Messaging:Enabled"] = messaging.ToString(),
                ["Messaging:OutboxPollInterval"] = "00:00:00.100",
                ["Service:Name"] = serviceName,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddRecuroApplication(typeof(TestHost).Assembly);
        services.AddRecuroPersistence<TestDbContext>(configuration, "test");
        services.AddRecuroMessaging(configuration)
            .Subscribe<WidgetCreatedData, RecordReceivedWidget>(PublishWidgetCreated.EventType);
        return services.BuildServiceProvider();
    }

    public static async Task CreateDatabaseAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public static AsyncServiceScope ScopeFor(IServiceProvider provider, Guid tenantId, string user = "user-1")
    {
        var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
        context.SetTenant(tenantId);
        context.SetUser(user, "Test User", ["hrta"]);
        context.SetCorrelation("req-1", "corr-1");
        return scope;
    }
}
