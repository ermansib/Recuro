using System.Text.Json.Serialization;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Domain.Emails;

namespace Recuro.Notification.Application.Emails;

/// <summary>The frontend's <c>EmailMessage</c> (frontend/src/domain/types.ts), field for field.</summary>
public sealed record EmailMessageDto(
    string Id,
    string RecipientRole,
    string Tag,
    string From,
    string To,
    string Subject,
    IReadOnlyList<string> Paragraphs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cta,
    string Signature,
    string CreatedAt,
    bool Unread)
{
    public static EmailMessageDto FromMessage(EmailMessage message, bool unread)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(
            message.Id.ToString(),
            message.RecipientRole,
            message.Tag,
            $"{message.FromName} <{message.FromAddress}>",
            message.ToAddress ?? string.Empty,
            message.Subject,
            message.Paragraphs,
            message.Cta,
            message.Signature,
            Timestamps.Format(message.CreatedAt),
            unread);
    }
}

/// <summary>One delivery log row (RCU-NTF-004) for auditors.</summary>
public sealed record DeliveryLogEntryDto(
    string Id,
    string TemplateKey,
    string TemplateVersion,
    string MatrixVersion,
    string SourceEventId,
    string SourceEventType,
    string RecipientRole,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RecipientUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To,
    string Status,
    int Attempts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProviderMessageId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastError,
    string CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SentAt)
{
    public static DeliveryLogEntryDto From(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(
            message.Id.ToString(),
            message.TemplateKey,
            message.TemplateVersion,
            message.MatrixVersion,
            message.SourceEventId.ToString(),
            message.SourceEventType,
            message.RecipientRole,
            message.RecipientUserId,
            message.ToAddress,
            message.Status.ToString(),
            message.Attempts,
            message.ProviderMessageId,
            message.LastError,
            Timestamps.Format(message.CreatedAt),
            message.SentAt is { } sent ? Timestamps.Format(sent) : null);
    }
}
