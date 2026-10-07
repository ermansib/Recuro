using System.Text.Json;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets;

/// <summary>
/// A version as the admin screens see it. <see cref="EffectiveTo"/> is computed: the effective date of
/// the next active version, or null while this one is the latest.
/// </summary>
public sealed record RuleSetVersionDto(
    Guid Id,
    string MatrixType,
    int Number,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Note,
    string ProposedBy,
    DateTimeOffset ProposedAt,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? RejectionReason,
    JsonElement Content)
{
    public static RuleSetVersionDto From(RuleSetVersion version, IEnumerable<RuleSetVersion> siblings)
    {
        ArgumentNullException.ThrowIfNull(version);
        DateTimeOffset? effectiveTo = version.Status == VersionStatus.Active
            ? siblings
                .Where(s => s.Status == VersionStatus.Active && s.Id != version.Id && s.EffectiveFrom > version.EffectiveFrom)
                .Select(s => (DateTimeOffset?)s.EffectiveFrom)
                .Min()
            : null;

        return new RuleSetVersionDto(
            version.Id,
            version.MatrixType.ToKey(),
            version.Number,
            version.Status.ToString(),
            version.EffectiveFrom,
            effectiveTo,
            version.Note,
            version.ProposedByName,
            version.ProposedAt,
            version.DecidedByName,
            version.DecidedAt,
            version.RejectionReason,
            MatrixJson.ToElement(version.Content));
    }
}
