using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;

namespace Recuro.Notification.Application.Events;

/// <summary>
/// The fields this service reads from <c>identity.user.provisioned.v1</c> and <c>identity.role.changed.v1</c>
/// (tolerant reader: anything else in the payload is ignored; missing fields keep their old value).
/// </summary>
public sealed record IdentityUserPayload(string? UserId, string? Name, string? Email, IReadOnlyList<string>? Roles);

/// <summary>Keeps the local recipient directory in step with Identity, so role recipients get email addresses.</summary>
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

        user.Update(data.Name, data.Email, data.Roles, now);
    }
}
