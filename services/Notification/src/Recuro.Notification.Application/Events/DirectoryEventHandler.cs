using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;

namespace Recuro.Notification.Application.Events;

/// <summary>
/// The fields this service reads from <c>identity.user.provisioned.v1</c> (<c>userId</c>, <c>roles</c>) and
/// <c>identity.role.changed.v1</c> (<c>userId</c>, <c>to</c>). Tolerant reader: other fields are ignored,
/// and <c>name</c>/<c>email</c> are used if a future version adds them.
/// </summary>
public sealed record IdentityUserPayload(string? UserId, string? Name, string? Email, IReadOnlyList<string>? Roles, IReadOnlyList<string>? To)
{
    /// <summary>The user's roles now: <c>roles</c> on provisioning, <c>to</c> on a change.</summary>
    public IReadOnlyList<string>? CurrentRoles => Roles ?? To;
}

/// <summary>Keeps the local recipient directory (who holds which role) in step with Identity's events.</summary>
public sealed class DirectoryEventHandler(INotificationStore store, TimeProvider clock) : IIntegrationEventHandler<IdentityUserPayload>
{
    public async Task Handle(IntegrationEvent<IdentityUserPayload> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var data = integrationEvent.Data;
        var userId = data.UserId ?? integrationEvent.Metadata.Subject[(integrationEvent.Metadata.Subject.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var user = await store.FindUserAsync(userId, ct);
        if (user is null)
        {
            user = DirectoryUser.Create(userId, now);
            store.Add(user);
        }

        user.Update(data.Name, data.Email, data.CurrentRoles, now);
    }
}
