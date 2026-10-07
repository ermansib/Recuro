using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Sends a domain event to every <see cref="IDomainEventHandler{TEvent}"/> registered for its type.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct);
}

internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct) =>
        Invokers.GetOrAdd(domainEvent.GetType(), CreateInvoker)(services, domainEvent, ct);

    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> CreateInvoker(Type eventType) =>
        typeof(DomainEventDispatcher)
            .GetMethod(nameof(InvokeAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>();

    private static async Task InvokeAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.Handle((TEvent)domainEvent, ct);
        }
    }
}

/// <summary>For design-time contexts (migrations) where nothing is dispatched.</summary>
public sealed class NoDomainEvents : IDomainEventDispatcher
{
    public static readonly NoDomainEvents Instance = new();

    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct) => Task.CompletedTask;
}
