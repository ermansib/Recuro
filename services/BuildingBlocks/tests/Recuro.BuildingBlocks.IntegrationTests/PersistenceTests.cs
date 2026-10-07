using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure.Messaging;

namespace Recuro.BuildingBlocks.IntegrationTests;

[Collection(ContainersCollection.Name)]
public sealed class PersistenceTests(ContainersFixture containers) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _provider = TestHost.Build(containers, $"persistence_{Guid.NewGuid():N}", messaging: false);
        await TestHost.CreateDatabaseAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_tenant_and_other_tenants_cannot_see_them()
    {
        await using (var scope = TestHost.ScopeFor(_provider, TestHost.TenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.Widgets.Add(Widget.Create("alpha"));
            await db.SaveChangesAsync();
        }

        await using (var scope = TestHost.ScopeFor(_provider, TestHost.TenantA))
        {
            var widget = await scope.ServiceProvider.GetRequiredService<TestDbContext>().Widgets.SingleAsync();
            Assert.Equal(TestHost.TenantA, widget.TenantId);
        }

        await using (var scope = TestHost.ScopeFor(_provider, TestHost.TenantB))
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<TestDbContext>().Widgets.ToListAsync());
        }
    }

    [Fact]
    public async Task Without_a_tenant_queries_return_nothing()
    {
        await using (var scope = TestHost.ScopeFor(_provider, TestHost.TenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.Widgets.Add(Widget.Create("beta"));
            await db.SaveChangesAsync();
        }

        await using var anonymous = _provider.CreateAsyncScope();
        Assert.Empty(await anonymous.ServiceProvider.GetRequiredService<TestDbContext>().Widgets.ToListAsync());
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_throws()
    {
        await using var scope = TestHost.ScopeFor(_provider, TestHost.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        db.Widgets.Add(Widget.Create("gamma", TestHost.TenantB));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Domain_event_writes_a_cloud_event_to_the_outbox_in_the_same_transaction()
    {
        Guid widgetId;
        await using (var scope = TestHost.ScopeFor(_provider, TestHost.TenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var widget = Widget.Create("delta");
            widgetId = widget.Id;
            db.Widgets.Add(widget);
            await db.SaveChangesAsync();
        }

        await using var check = _provider.CreateAsyncScope();
        var messages = await check.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.ToListAsync();
        var message = Assert.Single(messages, m => m.Envelope.Contains(widgetId.ToString(), StringComparison.Ordinal));
        var cloudEvent = EventJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(message.Envelope));

        Assert.Equal("1.0", cloudEvent.SpecVersion);
        Assert.Equal(PublishWidgetCreated.EventType, cloudEvent.Type);
        Assert.Equal("/services/widgets", cloudEvent.Source);
        Assert.Equal($"Widget/{widgetId}", cloudEvent.Subject);
        Assert.Equal(TestHost.TenantA, cloudEvent.TenantId);
        Assert.Equal("corr-1", cloudEvent.CorrelationId);
        Assert.Equal("user-1", cloudEvent.ActorId);
        Assert.Equal("delta", cloudEvent.Data.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rolled_back_change_leaves_no_outbox_row()
    {
        await using var scope = TestHost.ScopeFor(_provider, TestHost.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var before = await db.OutboxMessages.CountAsync();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Widgets.Add(Widget.Create("epsilon"));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.Equal(before, await db.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task A_redelivered_event_is_handled_once()
    {
        var processor = _provider.GetRequiredService<IntegrationEventProcessor>();
        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = "/services/other",
            Type = PublishWidgetCreated.EventType,
            Subject = "Widget/1",
            Time = DateTimeOffset.UtcNow,
            TenantId = TestHost.TenantB,
            ActorId = "actor-9",
            Data = JsonSerializer.SerializeToElement(new WidgetCreatedData(Guid.NewGuid(), "zeta"), EventJson.Options),
        };

        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);

        await using var scope = TestHost.ScopeFor(_provider, TestHost.TenantB);
        var received = await scope.ServiceProvider.GetRequiredService<TestDbContext>().Received.ToListAsync();
        var only = Assert.Single(received);
        Assert.Equal("actor-9", only.Actor);
        Assert.Equal(TestHost.TenantB, only.TenantId);
    }
}
