using System.Globalization;
using System.Text.Json;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Feed;
using Recuro.Notification.Domain.Matrix;

namespace Recuro.Notification.Application.Events;

/// <summary>Who receives one rule's notification in the bell, and who gets an email.</summary>
public sealed record ResolvedRecipients(IReadOnlyList<Recipient> InApp, IReadOnlyList<Recipient> Email);

/// <summary>
/// Turns a matrix <see cref="RecipientRule"/> into people (RCU-NTF-001 "role-in-context"). A role gets
/// one role-wide bell item and one email per person the directory knows in that role; a named user gets
/// both addressed to them, and a payload value that names a staff role (Workflow's assignees) reaches
/// that role. People the directory does not know yet still get their bell item; their email is logged
/// as suppressed for lack of an address.
/// </summary>
public sealed class RecipientResolver(INotificationStore store, ICandidateContacts candidates)
{
    public async Task<ResolvedRecipients> ResolveAsync(RecipientRule rule, EventMetadata metadata, JsonElement data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(metadata);
        if (rule.Kind == RecipientKind.PayloadCandidate)
        {
            return await ForCandidateAsync(rule, data, ct);
        }

        var userIds = rule.Kind switch
        {
            RecipientKind.Role => null,
            RecipientKind.Actor => Single(metadata.ActorId),
            RecipientKind.PayloadUser => Single(EventPayload.ReadString(data, rule.Field)),
            RecipientKind.PayloadUsers => EventPayload.ReadStrings(data, rule.Field),
            RecipientKind.SubjectOwner => Single((await store.FindOwnerAsync(metadata.Subject, ct))?.UserId),
            RecipientKind.PayloadCandidate => throw new InvalidOperationException("Handled above."),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Kind, "Unknown recipient kind."),
        };

        if (userIds is null || (userIds.Count == 0 && rule.FallbackToRole))
        {
            return await ForRoleAsync(rule.Role, ct);
        }

        // Workflow assigns tasks to roles (e.g. "hrhead"), so a payload value naming a staff role reaches that
        // role; anything else is a user id.
        var inApp = new List<Recipient>();
        var email = new List<Recipient>();
        foreach (var value in userIds.Distinct(StringComparer.Ordinal))
        {
            if (NotificationMatrix.BroadcastRoles.Contains(value))
            {
                var role = await ForRoleAsync(value, ct);
                inApp.AddRange(role.InApp);
                email.AddRange(role.Email);
                continue;
            }

            var known = await store.FindUserAsync(value, ct);
            var name = known?.Name ?? (value == metadata.ActorId ? metadata.ActorName : null);
            var person = new Recipient(rule.Role, value, name, known?.Email);
            inApp.Add(person);
            email.Add(person);
        }

        return new ResolvedRecipients(inApp, email);
    }

    /// <summary>
    /// A candidate record is not a portal account: the email goes to Candidate's address for it, and it
    /// never shows in anyone's bell or email centre (its key is <c>candidate:&lt;id&gt;</c>).
    /// </summary>
    private async Task<ResolvedRecipients> ForCandidateAsync(RecipientRule rule, JsonElement data, CancellationToken ct)
    {
        var candidateId = EventPayload.ReadString(data, rule.Field);
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            return new ResolvedRecipients([], []);
        }

        var contact = await candidates.FindAsync(candidateId, ct);
        var recipient = new Recipient(rule.Role, $"candidate:{candidateId}", contact?.Name, contact?.Email);
        return new ResolvedRecipients([], [recipient]);
    }

    private async Task<ResolvedRecipients> ForRoleAsync(string role, CancellationToken ct)
    {
        var members = await store.UsersInRoleAsync(role, ct);
        IReadOnlyList<Recipient> email = members.Count == 0
            ? [Recipient.Everyone(role)]
            : members.Select(m => ToRecipient(role, m)).ToList();
        return new ResolvedRecipients([Recipient.Everyone(role)], email);
    }

    private static Recipient ToRecipient(string role, DirectoryUser user) => new(role, user.UserId, user.Name, user.Email);

    private static List<string> Single(string? userId) => string.IsNullOrWhiteSpace(userId) ? [] : [userId];
}

/// <summary>Tolerant reads of event payload fields (each consumer reads only what it needs).</summary>
public static class EventPayload
{
    public static string? ReadString(JsonElement data, string? field) =>
        field is not null && data.ValueKind == JsonValueKind.Object && data.TryGetProperty(field, out var value)
            ? AsText(value)
            : null;

    public static List<string> ReadStrings(JsonElement data, string? field)
    {
        if (field is null || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(field, out var value))
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(AsText).OfType<string>().ToList()
            : AsText(value) is { } single ? [single] : [];
    }

    /// <summary>A date (<c>YYYY-MM-DD</c>, as midnight UTC) or timestamp field; null when absent or unreadable.</summary>
    public static DateTimeOffset? ReadDate(JsonElement data, string? field) =>
        ReadString(data, field) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    /// <summary>Template values: the payload's top-level scalar fields plus the subject id and actor name.</summary>
    public static IReadOnlyDictionary<string, string> TemplateValues(EventMetadata metadata, JsonElement data)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subjectId"] = metadata.Subject[(metadata.Subject.LastIndexOf('/') + 1)..],
            ["actorName"] = metadata.ActorName ?? string.Empty,
        };

        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in data.EnumerateObject())
            {
                var text = property.Value.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", property.Value.EnumerateArray().Select(AsText).OfType<string>())
                    : AsText(property.Value);
                if (text is not null)
                {
                    values[property.Name] = text;
                }
            }
        }

        return values;
    }

    private static string? AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null,
    };
}
