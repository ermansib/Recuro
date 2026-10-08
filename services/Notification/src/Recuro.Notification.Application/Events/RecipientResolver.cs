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
public sealed class RecipientResolver(INotificationStore store, ICandidateContacts candidates, IStaffDirectory staff, TimeProvider clock)
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
        var members = await staff.UsersInRoleAsync(role, ct) is { } current
            ? await RememberAsync(role, current, ct)
            : await store.UsersInRoleAsync(role, ct);
        IReadOnlyList<Recipient> email = members.Count == 0
            ? [Recipient.Everyone(role)]
            : members.Select(m => ToRecipient(role, m)).ToList();
        return new ResolvedRecipients([Recipient.Everyone(role)], email);
    }

    /// <summary>Keeps the local directory in step with Identity, so it can stand in when Identity can't answer.</summary>
    private async Task<IReadOnlyList<DirectoryUser>> RememberAsync(string role, IReadOnlyList<StaffContact> people, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var members = new List<DirectoryUser>();
        foreach (var person in people.Where(p => !string.IsNullOrWhiteSpace(p.UserId)).DistinctBy(p => p.UserId, StringComparer.Ordinal))
        {
            var entry = await store.FindUserAsync(person.UserId, ct);
            if (entry is null)
            {
                entry = DirectoryUser.Create(person.UserId, now);
                store.Add(entry);
            }

            entry.Update(person.Name, person.Email, entry.Roles.Contains(role, StringComparer.Ordinal) ? null : [.. entry.Roles, role], now);
            members.Add(entry);
        }

        return members;
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

    /// <summary>
    /// Template values: the payload's top-level scalar fields plus the subject id and actor name. A list reads
    /// as its items (objects by their <c>label</c>); with <paramref name="linkBase"/>, a gateway path field such
    /// as <c>pdfPath</c> also reads as an absolute link <c>{pdfUrl}</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TemplateValues(EventMetadata metadata, JsonElement data, Uri? linkBase = null)
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
                    ? string.Join(", ", property.Value.EnumerateArray().Select(AsListItem).OfType<string>())
                    : AsText(property.Value);
                if (text is not null)
                {
                    values[property.Name] = text;
                }

                // Durations travel as milliseconds (e.g. Pipeline's varianceMs); templates show them readably as {variance}.
                if (property.Name.Length > 2 && property.Name.EndsWith("Ms", StringComparison.Ordinal)
                    && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var ms))
                {
                    values.TryAdd(property.Name[..^2], Duration(TimeSpan.FromMilliseconds(ms)));
                }

                // Download paths travel gateway-relative (e.g. Reporting's pdfPath); emails need them absolute as {pdfUrl}.
                if (linkBase is not null && property.Name.Length > 4 && property.Name.EndsWith("Path", StringComparison.Ordinal)
                    && text is not null && text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal))
                {
                    values.TryAdd($"{property.Name[..^4]}Url", new Uri(linkBase, text.TrimStart('/')).ToString());
                }
            }
        }

        return values;
    }

    /// <summary>"2d 4h", "3h 20m" or "45m": the two largest units, enough for a TAT overrun.</summary>
    public static string Duration(TimeSpan span)
    {
        var sign = span < TimeSpan.Zero ? "-" : string.Empty;
        span = span.Duration();
        return span.TotalDays >= 1 ? $"{sign}{(int)span.TotalDays}d {span.Hours}h"
            : span.TotalHours >= 1 ? $"{sign}{(int)span.TotalHours}h {span.Minutes}m"
            : $"{sign}{(int)span.TotalMinutes}m";
    }

    private static string? AsListItem(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("label", out var label) ? AsText(label) : AsText(value);

    private static string? AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null,
    };
}
