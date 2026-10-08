using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Inbox;
using Recuro.BuildingBlocks.Infrastructure.Outbox;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Runs every matching handler for a received event, transport-agnostic (the RabbitMQ consumer and
/// tests both call it). Each handler gets its own scope with the event's tenant and actor, and runs in
/// one transaction with its inbox row, so a redelivered event is processed at most once per handler.
/// </summary>
public sealed class IntegrationEventProcessor(IServiceScopeFactory scopes, EventSubscriptions subscriptions, TimeProvider clock)
{
    public async Task ProcessAsync(CloudEvent cloudEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cloudEvent);
        ActivityContext.TryParse(cloudEvent.TraceParent, null, out var parent);
        using var activity = MessagingTelemetry.Source.StartActivity($"consume {cloudEvent.Type}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.message.id", cloudEvent.Id.ToString());

        foreach (var subscription in subscriptions.Items.Where(s => s.Matches(cloudEvent.Type)))
        {
            await RunHandlerAsync(subscription, cloudEvent, ct);
        }
    }

    private async Task RunHandlerAsync(EventSubscription subscription, CloudEvent cloudEvent, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
        context.SetTenant(cloudEvent.TenantId);
        context.SetUser(cloudEvent.ActorId, cloudEvent.ActorName, cloudEvent.ActorRole is null ? [] : [cloudEvent.ActorRole]);
        context.SetCorrelation(cloudEvent.Id.ToString(), cloudEvent.CorrelationId);
        context.MakeCurrent();

        var db = scope.ServiceProvider.GetRequiredService<RecuroDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var seen = await db.InboxMessages.AnyAsync(
                m => m.EventId == cloudEvent.Id && m.Consumer == subscription.ConsumerName, ct);
            if (seen)
            {
                return;
            }

            await subscription.Invoke(scope.ServiceProvider, cloudEvent.ToMetadata(), cloudEvent.Data, ct);
            db.InboxMessages.Add(new InboxMessage(cloudEvent.Id, subscription.ConsumerName, cloudEvent.Type, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}
