using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.Notification.Domain.Emails;

namespace Recuro.Notification.Application.Events;

/// <summary>Payload of <c>notification.email.dispatched.v1</c> and <c>notification.email.failed.v1</c>. No addresses or text.</summary>
public sealed record EmailOutcomePayload(
    Guid EmailId,
    string TemplateKey,
    string TemplateVersion,
    string RecipientRole,
    string? RecipientUserId,
    string Status,
    int Attempts,
    string? ProviderMessageId,
    Guid SourceEventId);

/// <summary>RCU-NTF-002: tells the platform an email went out (through the outbox, in the same transaction).</summary>
internal sealed class EmailDispatchedHandler(IIntegrationEventPublisher publisher) : IDomainEventHandler<EmailDispatched>
{
    public Task Handle(EmailDispatched domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        publisher.Publish(NotificationEventTypes.EmailDispatched, $"Email/{domainEvent.Message.Id}", EmailOutcome.From(domainEvent.Message));
        return Task.CompletedTask;
    }
}

/// <summary>RCU-NTF-002: after the last retry the email is dead-lettered and <c>notification.email.failed.v1</c> goes out.</summary>
internal sealed class EmailFailedHandler(IIntegrationEventPublisher publisher) : IDomainEventHandler<EmailFailed>
{
    public Task Handle(EmailFailed domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        publisher.Publish(NotificationEventTypes.EmailFailed, $"Email/{domainEvent.Message.Id}", EmailOutcome.From(domainEvent.Message));
        return Task.CompletedTask;
    }
}

internal static class EmailOutcome
{
    public static EmailOutcomePayload From(EmailMessage message) => new(
        message.Id,
        message.TemplateKey,
        message.TemplateVersion,
        message.RecipientRole,
        message.RecipientUserId,
        message.Status.ToString(),
        message.Attempts,
        message.ProviderMessageId,
        message.SourceEventId);
}
