using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Application.Users.Events;

/// <summary>Payload of <c>identity.user.provisioned.v1</c>. Ids and roles only, no PII.</summary>
public sealed record UserProvisionedPayload(string UserId, IReadOnlyList<string> Roles);

/// <summary>Payload of <c>identity.role.changed.v1</c>.</summary>
public sealed record RoleChangedPayload(string UserId, IReadOnlyList<string> From, IReadOnlyList<string> To);

/// <summary>Turns user-mirror domain events into catalog integration events, in the same transaction (outbox).</summary>
internal sealed class UserEventsPublisher(IIntegrationEventPublisher events) :
    IDomainEventHandler<UserProvisionedDomainEvent>,
    IDomainEventHandler<UserRolesChangedDomainEvent>
{
    public Task Handle(UserProvisionedDomainEvent domainEvent, CancellationToken ct)
    {
        events.Publish(EventTypes.Identity.UserProvisioned, Subject(domainEvent.Subject), new UserProvisionedPayload(domainEvent.Subject, domainEvent.Roles));
        return Task.CompletedTask;
    }

    public Task Handle(UserRolesChangedDomainEvent domainEvent, CancellationToken ct)
    {
        events.Publish(EventTypes.Identity.RoleChanged, Subject(domainEvent.Subject), new RoleChangedPayload(domainEvent.Subject, domainEvent.From, domainEvent.To));
        return Task.CompletedTask;
    }

    private static string Subject(string userId) => $"User/{userId}";
}
