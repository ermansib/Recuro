using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;
using Recuro.Notification.Domain.Matrix;

namespace Recuro.Notification.Application.Events;

/// <summary>
/// RCU-NTF-001: applies the §5.6 matrix to every subscribed event. For each matching rule it resolves
/// recipients, renders the template and creates bell items and email jobs, then publishes
/// <c>notification.created.v1</c> per bell item and wakes live streams. Runs once per event (inbox), and
/// the unique (event, template, recipient) indexes make the fan-out idempotent even so.
/// </summary>
public sealed partial class MatrixEventHandler(
    INotificationStore store,
    RecipientResolver resolver,
    ITemplateSource templates,
    IIntegrationEventPublisher publisher,
    IFeedChangeSignal feedSignal,
    EmailOptions emailOptions,
    TimeProvider clock,
    ILogger<MatrixEventHandler> logger) : IIntegrationEventHandler<JsonElement>
{
    public async Task Handle(IntegrationEvent<JsonElement> integrationEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (metadata, data) = (integrationEvent.Metadata, integrationEvent.Data);
        var now = clock.GetUtcNow();

        await RecordOwnerAsync(metadata, now, ct);

        var values = EventPayload.TemplateValues(metadata, data);
        var bellItems = 0;
        foreach (var rule in NotificationMatrix.RulesFor(metadata.Type))
        {
            var template = await templates.GetAsync(rule.TemplateKey, ct)
                ?? throw new InvalidOperationException($"No template '{rule.TemplateKey}' for {rule.EventType}.");
            var origin = DeliveryOrigin.From(metadata.Id, rule, template.Version);
            var inAppKeys = new HashSet<string>(StringComparer.Ordinal);
            var emailKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var recipientRule in rule.Recipients)
            {
                var recipients = await resolver.ResolveAsync(recipientRule, metadata, data, ct);
                if (rule.Channels.HasFlag(Channels.InApp))
                {
                    var content = template.RenderNotification(values);
                    foreach (var recipient in recipients.InApp.Where(r => inAppKeys.Add(r.Key)))
                    {
                        bellItems += AddFeedItem(recipient, content, origin, now) ? 1 : 0;
                    }
                }

                if (rule.Channels.HasFlag(Channels.Email))
                {
                    var content = template.RenderEmail(values);
                    foreach (var recipient in recipients.Email.Where(r => emailKeys.Add(r.Key)))
                    {
                        await AddEmailAsync(recipient, content, origin, now, ct);
                    }
                }
            }
        }

        if (bellItems > 0)
        {
            await feedSignal.SignalAsync(metadata.TenantId, ct);
        }
    }

    private async Task RecordOwnerAsync(EventMetadata metadata, DateTimeOffset now, CancellationToken ct)
    {
        if (!NotificationMatrix.OwnerEvents.Contains(metadata.Type) || string.IsNullOrWhiteSpace(metadata.ActorId))
        {
            return;
        }

        if (await store.FindOwnerAsync(metadata.Subject, ct) is null)
        {
            store.Add(SubjectOwner.Record(metadata.Subject, metadata.ActorId, metadata.ActorName, now));
        }
    }

    private bool AddFeedItem(Recipient recipient, RenderedNotification content, DeliveryOrigin origin, DateTimeOffset now)
    {
        var created = FeedItem.Create(recipient, content, origin, now);
        if (created.IsFailure)
        {
            Skipped(logger, origin.TemplateKey, recipient.Key, created.Error!.Message);
            return false;
        }

        var item = created.Value;
        store.Add(item);
        publisher.Publish(
            NotificationEventTypes.Created,
            $"Notification/{item.Id}",
            new NotificationCreatedPayload(item.Id, item.RecipientRole, item.RecipientUserId, item.TemplateKey, item.Critical, origin.EventId));
        return true;
    }

    private async Task AddEmailAsync(Recipient recipient, RenderedEmail content, DeliveryOrigin origin, DateTimeOffset now, CancellationToken ct)
    {
        var options = emailOptions;
        var created = EmailMessage.Create(recipient, content, new EmailSender(options.FromAddress, options.FromName, options.Signature), origin, now);
        if (created.IsFailure)
        {
            Skipped(logger, origin.TemplateKey, recipient.Key, created.Error!.Message);
            return;
        }

        var message = created.Value;
        if (message.ToAddress is { } address && (await store.FindContactAsync(address, ct))?.Bounced == true)
        {
            message.Suppress(SuppressionReasons.Bounced, now);
        }

        store.Add(message);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {TemplateKey} for {RecipientKey}: {Reason}")]
    private static partial void Skipped(ILogger logger, string templateKey, string recipientKey, string reason);
}

/// <summary>Event names this service publishes (from the catalog).</summary>
public static class NotificationEventTypes
{
    public const string Created = EventTypes.Notification.Created;
    public const string EmailDispatched = EventTypes.Notification.EmailDispatched;
    public const string EmailFailed = EventTypes.Notification.EmailFailed;
}

/// <summary>Payload of <c>notification.created.v1</c>. Ids only, no text or contact details.</summary>
public sealed record NotificationCreatedPayload(
    Guid NotificationId,
    string RecipientRole,
    string? RecipientUserId,
    string TemplateKey,
    bool Critical,
    Guid SourceEventId);
