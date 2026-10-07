using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.BuildingBlocks.IntegrationTests;

public sealed record WidgetCreated(Guid WidgetId, string Name) : IDomainEvent;

public sealed record WidgetCreatedData(Guid WidgetId, string Name);

public sealed class Widget : AggregateRoot, ITenantOwned
{
    private Widget()
    {
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public static Widget Create(string name, Guid tenantId = default)
    {
        var widget = new Widget { Id = Guid.CreateVersion7(), Name = name, TenantId = tenantId };
        widget.Raise(new WidgetCreated(widget.Id, name));
        return widget;
    }
}

/// <summary>Domain event → integration event, the pattern every service uses.</summary>
public sealed class PublishWidgetCreated(IIntegrationEventPublisher publisher) : IDomainEventHandler<WidgetCreated>
{
    public const string EventType = "test.widget.created.v1";

    public Task Handle(WidgetCreated domainEvent, CancellationToken ct)
    {
        publisher.Publish(EventType, $"Widget/{domainEvent.WidgetId}", new WidgetCreatedData(domainEvent.WidgetId, domainEvent.Name));
        return Task.CompletedTask;
    }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<ReceivedWidget> Received => Set<ReceivedWidget>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>().Ignore(w => w.DomainEvents);
        modelBuilder.Entity<ReceivedWidget>().HasKey(r => r.Id);
    }
}

/// <summary>What the consumer side writes, so tests can count handler runs.</summary>
public sealed class ReceivedWidget : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid WidgetId { get; set; }

    public string? Actor { get; set; }
}

public sealed class RecordReceivedWidget(TestDbContext db, ICurrentUser user) : IIntegrationEventHandler<WidgetCreatedData>
{
    public Task Handle(IntegrationEvent<WidgetCreatedData> integrationEvent, CancellationToken ct)
    {
        db.Received.Add(new ReceivedWidget { WidgetId = integrationEvent.Data.WidgetId, Actor = user.UserId });
        return Task.CompletedTask;
    }
}
