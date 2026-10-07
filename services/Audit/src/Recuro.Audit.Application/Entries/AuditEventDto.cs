using System.Text.Json.Serialization;
using Recuro.Audit.Domain.Entries;

namespace Recuro.Audit.Application.Entries;

/// <summary>
/// The frontend's <c>AuditEvent</c> (frontend/src/domain/types.ts), field for field. Optional fields
/// are left out when empty, like the mock does.
/// </summary>
public sealed record AuditEventDto(
    string Id,
    string Actor,
    string Role,
    string At,
    string Entity,
    string Action,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Before,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? After,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    string ConfigVersion)
{
    public static AuditEventDto From(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new AuditEventDto(
            entry.Id.ToString(),
            entry.ActorName,
            entry.ActorRole ?? string.Empty,
            entry.OccurredAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            entry.Entity,
            entry.Action,
            entry.Before,
            entry.After,
            entry.Reason,
            entry.ConfigVersion ?? string.Empty);
    }
}
