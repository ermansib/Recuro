using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.IntegrationEvents;

namespace Recuro.BuildingBlocks.Infrastructure.Messaging;

/// <summary>One handler subscribed to one event type (or <see cref="EventTypes.All"/>).</summary>
public sealed record EventSubscription(
    string EventType,
    string ConsumerName,
    Func<IServiceProvider, EventMetadata, JsonElement, CancellationToken, Task> Invoke)
{
    public bool Matches(string type) => EventType == EventTypes.All || EventType == type;
}

/// <summary>Every subscription in this service. Its event types become the queue's bindings.</summary>
public sealed class EventSubscriptions
{
    private readonly List<EventSubscription> _items = [];

    public IReadOnlyList<EventSubscription> Items => _items;

    public IEnumerable<string> BindingKeys => _items.Select(s => s.EventType).Distinct(StringComparer.Ordinal);

    internal void Add<TData, THandler>(string eventType)
        where THandler : IIntegrationEventHandler<TData>
    {
        _items.Add(new EventSubscription(
            eventType,
            typeof(THandler).FullName!,
            static async (services, metadata, data, ct) =>
            {
                var payload = data.Deserialize<TData>(EventJson.Options)
                    ?? throw new JsonException($"Event {metadata.Id} has no data.");
                await services.GetRequiredService<THandler>().Handle(new IntegrationEvent<TData>(metadata, payload), ct);
            }));
    }
}
