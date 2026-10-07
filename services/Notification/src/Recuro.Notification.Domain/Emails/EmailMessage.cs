using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Domain.Emails;

/// <summary>Email delivery states (RCU-NTF-002/004).</summary>
public enum EmailStatus
{
    /// <summary>Waiting to be sent, or waiting for the next retry.</summary>
    Pending,

    /// <summary>The provider accepted it; <see cref="EmailMessage.ProviderMessageId"/> is set.</summary>
    Sent,

    /// <summary>Gave up after the last retry. The dead-letter state: an operator can see it, nothing resends it.</summary>
    Failed,

    /// <summary>Not sent on purpose: no address, a bounced address, or a muted category. Kept for the record.</summary>
    Suppressed,
}

/// <summary>
/// One email to one recipient, and its delivery log row (RCU-NTF-004): template, version, recipient,
/// timestamps, provider message id and status. Rows are never deleted, so the log outlives retention
/// of the things it talks about.
/// </summary>
public sealed class EmailMessage : AggregateRoot, ITenantOwned
{
    public const int MaxAttempts = 3;

    private static readonly TransitionTable<EmailStatus> Transitions = new(new Dictionary<EmailStatus, EmailStatus[]>
    {
        [EmailStatus.Pending] = [EmailStatus.Pending, EmailStatus.Sent, EmailStatus.Failed, EmailStatus.Suppressed],
        [EmailStatus.Sent] = [],
        [EmailStatus.Failed] = [],
        [EmailStatus.Suppressed] = [],
    });

    private string[] _paragraphs = [];

    private EmailMessage()
    {
    }

    public Guid TenantId { get; private set; }

    public string RecipientRole { get; private set; } = string.Empty;

    public string? RecipientUserId { get; private set; }

    public string RecipientKey { get; private set; } = string.Empty;

    public string? ToAddress { get; private set; }

    public string? ToName { get; private set; }

    public string FromAddress { get; private set; } = string.Empty;

    public string FromName { get; private set; } = string.Empty;

    public string Tag { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public IReadOnlyList<string> Paragraphs => _paragraphs;

    public string? Cta { get; private set; }

    public string? Link { get; private set; }

    public string Signature { get; private set; } = string.Empty;

    public string TemplateKey { get; private set; } = string.Empty;

    public string TemplateVersion { get; private set; } = string.Empty;

    public string MatrixVersion { get; private set; } = string.Empty;

    public bool Critical { get; private set; }

    public Guid SourceEventId { get; private set; }

    public string SourceEventType { get; private set; } = string.Empty;

    public EmailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>When the dispatcher should next try. Also the earliest send time for scheduled mail.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static Result<EmailMessage> Create(
        Recipient recipient,
        RenderedEmail content,
        EmailSender sender,
        DeliveryOrigin origin,
        DateTimeOffset now,
        DateTimeOffset? sendAt = null)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(origin);
        var check = recipient.EnsureAddressable();
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var message = new EmailMessage
        {
            Id = Guid.CreateVersion7(),
            RecipientRole = recipient.Role,
            RecipientUserId = recipient.UserId,
            RecipientKey = recipient.Key,
            ToAddress = recipient.Email,
            ToName = recipient.Name,
            FromAddress = sender.Address,
            FromName = sender.Name,
            Tag = content.Tag,
            Subject = content.Subject,
            _paragraphs = [.. content.Paragraphs],
            Cta = content.Cta,
            Link = content.Link,
            Signature = sender.Signature,
            TemplateKey = origin.TemplateKey,
            TemplateVersion = origin.TemplateVersion,
            MatrixVersion = origin.MatrixVersion,
            Critical = origin.Critical,
            SourceEventId = origin.EventId,
            SourceEventType = origin.EventType,
            Status = EmailStatus.Pending,
            CreatedAt = now,
            NextAttemptAt = sendAt is { } at && at > now ? at : now,
        };

        if (string.IsNullOrWhiteSpace(recipient.Email))
        {
            message.Suppress(SuppressionReasons.NoAddress, now);
        }

        return message;
    }

    /// <summary>Due for sending at <paramref name="now"/>.</summary>
    public bool IsDue(DateTimeOffset now) => Status == EmailStatus.Pending && NextAttemptAt <= now;

    public Result MarkSent(string providerMessageId, DateTimeOffset now)
    {
        var move = Transitions.EnsureCanMove(Status, EmailStatus.Sent, nameof(EmailMessage));
        if (move.IsFailure)
        {
            return move;
        }

        Attempts++;
        Status = EmailStatus.Sent;
        ProviderMessageId = providerMessageId;
        SentAt = now;
        LastError = null;
        Raise(new EmailDispatched(this));
        return Result.Success();
    }

    /// <summary>
    /// Records a failed attempt. Retries with exponential backoff (1, 2, 4 minutes × <paramref name="backoffUnit"/>);
    /// after <see cref="MaxAttempts"/> the message is Failed and <see cref="EmailFailed"/> is raised.
    /// </summary>
    public Result RecordFailure(string error, DateTimeOffset now, TimeSpan backoffUnit)
    {
        var target = Attempts + 1 >= MaxAttempts ? EmailStatus.Failed : EmailStatus.Pending;
        var move = Transitions.EnsureCanMove(Status, target, nameof(EmailMessage));
        if (move.IsFailure)
        {
            return move;
        }

        Attempts++;
        LastError = error.Length <= 500 ? error : error[..500];
        Status = target;
        if (target == EmailStatus.Failed)
        {
            Raise(new EmailFailed(this));
        }
        else
        {
            NextAttemptAt = now + (backoffUnit * Math.Pow(2, Attempts - 1));
        }

        return Result.Success();
    }

    public Result Suppress(string reason, DateTimeOffset now)
    {
        var move = Transitions.EnsureCanMove(Status, EmailStatus.Suppressed, nameof(EmailMessage));
        if (move.IsFailure)
        {
            return move;
        }

        Status = EmailStatus.Suppressed;
        LastError = reason;
        NextAttemptAt = now;
        return Result.Success();
    }

    /// <summary>Keeps other replicas off this message while one is sending it.</summary>
    public void Claim(DateTimeOffset until) => NextAttemptAt = until;

    public bool IsVisibleTo(string userId, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return RecipientUserId is null ? roles.Contains(RecipientRole) : RecipientUserId == userId;
    }
}

/// <summary>What the matrix rendered for an email.</summary>
public sealed record RenderedEmail(string Tag, string Subject, IReadOnlyList<string> Paragraphs, string? Cta, string? Link);

/// <summary>The sending identity and signature (tenant branding overrides these later, RCU-CFG-004).</summary>
public sealed record EmailSender(string Address, string Name, string Signature);

public static class SuppressionReasons
{
    public const string NoAddress = "no_recipient_address";
    public const string Bounced = "address_bounced";
}

/// <summary>Raised when the provider accepts an email.</summary>
public sealed record EmailDispatched(EmailMessage Message) : IDomainEvent;

/// <summary>Raised when an email has used all its attempts.</summary>
public sealed record EmailFailed(EmailMessage Message) : IDomainEvent;
