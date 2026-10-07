using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Writes events to the outbox table of the service's own DbContext. Sent only if the transaction commits.</summary>
internal sealed class OutboxIntegrationEventPublisher(
    RecuroDbContext db,
    ScopeContext scope,
    TimeProvider clock,
    IOptions<ServiceIdentity> service) : IIntegrationEventPublisher
{
    public void Publish<TData>(string type, string subject, TData data)
        where TData : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var tenantId = ((ITenantContext)scope).RequiredTenantId;
        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = service.Value.Source,
            Type = type,
            Subject = subject,
            Time = clock.GetUtcNow(),
            DataSchema = $"events/{type}.schema.json",
            TenantId = tenantId,
            CorrelationId = scope.CorrelationId,
            TraceParent = Activity.Current?.Id,
            ActorId = scope.UserId,
            ActorName = scope.Name,
            ActorRole = scope.Roles.FirstOrDefault(),
            Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
        };

        db.OutboxMessages.Add(new OutboxMessage(cloudEvent.Id, tenantId, type, EventJson.Serialize(cloudEvent), cloudEvent.Time));
    }
}
