using Recuro.Notification.Application.Emails;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;
using Recuro.Notification.Domain.Templates;

namespace Recuro.Notification.Application.Abstractions;

/// <summary>Write side for the current tenant. Changes are committed by the unit of work.</summary>
public interface INotificationStore
{
    void Add(FeedItem item);

    void Add(EmailMessage message);

    void Add(ReadReceipt receipt);

    void Add(DirectoryUser user);

    void Add(SubjectOwner owner);

    void Add(ContactStatus contact);

    Task<DirectoryUser?> FindUserAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<DirectoryUser>> UsersInRoleAsync(string role, CancellationToken ct);

    Task<SubjectOwner?> FindOwnerAsync(string subject, CancellationToken ct);

    Task<ContactStatus?> FindContactAsync(string address, CancellationToken ct);

    Task<EmailMessage?> FindEmailAsync(Guid id, CancellationToken ct);

    /// <summary>Ids among <paramref name="itemIds"/> that <paramref name="userId"/> has already read.</summary>
    Task<IReadOnlySet<Guid>> ReadItemIdsAsync(string userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct);
}

/// <summary>Read side for the bell and the email centre. Never returns another tenant's rows.</summary>
public interface INotificationReadStore
{
    Task<IReadOnlyList<FeedEntry>> ListFeedAsync(Viewer viewer, FeedFilter filter, CancellationToken ct);

    /// <summary>Visible feed items after <paramref name="afterSequence"/>, oldest first (live stream resume).</summary>
    Task<IReadOnlyList<FeedEntry>> ListFeedSinceAsync(Viewer viewer, long afterSequence, int limit, CancellationToken ct);

    /// <summary>The newest sequence in the tenant, so a fresh stream starts from "now".</summary>
    Task<long> LatestSequenceAsync(CancellationToken ct);

    Task<IReadOnlyList<EmailMessageDto>> ListEmailsAsync(Viewer viewer, FeedFilter filter, CancellationToken ct);

    Task<UnreadCountDto> CountUnreadAsync(Viewer viewer, CancellationToken ct);

    /// <summary>Ids of the visible, unread feed items (or emails) for mark-all.</summary>
    Task<IReadOnlyList<Guid>> UnreadIdsAsync(Viewer viewer, bool emails, CancellationToken ct);

    /// <summary>Of <paramref name="ids"/>, the ones visible to the viewer.</summary>
    Task<IReadOnlyList<Guid>> VisibleIdsAsync(Viewer viewer, IReadOnlyCollection<Guid> ids, bool emails, CancellationToken ct);

    Task<IReadOnlyList<DeliveryLogEntryDto>> ListDeliveryLogAsync(DeliveryLogFilter filter, CancellationToken ct);
}

/// <summary>
/// Wakes live streams when feed items are added. <see cref="SignalAsync"/> runs inside the writing
/// transaction, so streams wake only after the items are committed.
/// </summary>
public interface IFeedChangeSignal
{
    Task SignalAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Waits for <see cref="IFeedChangeSignal"/> on any replica.</summary>
public interface IFeedChangeListener
{
    /// <summary>Completes when the tenant's feed changes or <paramref name="timeout"/> passes. True when it changed.</summary>
    Task<bool> WaitAsync(Guid tenantId, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Cached unread counts (RCU-NTF-003). Keys include tenant and user.</summary>
public interface IUnreadCountCache
{
    Task<UnreadCountDto?> GetAsync(Viewer viewer, CancellationToken ct);

    Task SetAsync(Viewer viewer, UnreadCountDto counts, CancellationToken ct);

    Task InvalidateAsync(Viewer viewer, CancellationToken ct);
}

/// <summary>
/// A candidate's email address for candidate-facing mail (regret emails). Candidate owns that PII; the
/// stand-in returns null until Candidate publishes a contact contract, and the email is then logged as
/// suppressed for lack of an address.
/// </summary>
public interface ICandidateContacts
{
    Task<CandidateContact?> FindAsync(string candidateId, CancellationToken ct);
}

public sealed record CandidateContact(string? Name, string Email);

/// <summary>Templates for the current tenant: its override (RCU-CFG-004, admin portal) first, then the default.</summary>
public interface ITemplateSource
{
    Task<NotificationTemplate?> GetAsync(string key, CancellationToken ct);
}

/// <summary>Sends one email through the provider (SMTP today). Throws on failure; returns the provider message id.</summary>
public interface IEmailTransport
{
    Task<string> SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Sending identity for outgoing mail (Options pattern, <c>Email</c> section).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string FromAddress { get; set; } = "no-reply@recuro.local";

    public string FromName { get; set; } = "Recuro";

    public string Signature { get; set; } = "Recuro Recruitment (automated)";

    /// <summary>Base of the retry backoff: attempts wait 1×, 2×, 4× this.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>The person reading their bell or email centre.</summary>
public sealed record Viewer(string UserId, IReadOnlyCollection<string> Roles);

/// <summary>Bell and email centre filters (RCU-NTF-003): unread only, page size, and a cursor.</summary>
public sealed record FeedFilter(bool UnreadOnly, int Limit, Guid? Before);

public sealed record DeliveryLogFilter(string? Status, string? TemplateKey, Guid? SourceEventId, int Limit);
