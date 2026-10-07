using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.Abstractions;

/// <summary>The current tenant's rule set versions. Never returns another tenant's rows.</summary>
public interface IRuleSetVersions
{
    Task<RuleSetVersion?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Every version of <paramref name="type"/>, newest number first.</summary>
    Task<IReadOnlyList<RuleSetVersion>> ListAsync(MatrixType type, CancellationToken ct);

    /// <summary>The active version in force at <paramref name="at"/>: the latest effective from on or before it.</summary>
    Task<RuleSetVersion?> FindActiveAtAsync(MatrixType type, DateTimeOffset at, CancellationToken ct);

    /// <summary>
    /// Creates the next version of <paramref name="type"/> and commits. Numbering is serialised per
    /// tenant and type; <paramref name="create"/> gets the next number and may return null to add nothing.
    /// </summary>
    Task<RuleSetVersion?> AddNextAsync(MatrixType type, Func<int, RuleSetVersion?> create, CancellationToken ct);

    /// <summary>Commits changes to loaded versions. False when someone else changed the same version first.</summary>
    Task<bool> TrySaveAsync(CancellationToken ct);
}
