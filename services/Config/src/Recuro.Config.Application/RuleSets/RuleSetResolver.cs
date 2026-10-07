using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets;

/// <summary>
/// Picks the version a resolution uses (RCU-CFG-002/003): the pinned <c>versionId</c> when given,
/// otherwise the active version in force at <c>at</c> (now by default). Drafts are never resolved.
/// </summary>
public sealed class RuleSetResolver(IRuleSetVersions versions, TimeProvider clock)
{
    public async Task<Result<RuleSetVersion>> ResolveAsync(MatrixType type, DateTimeOffset? at, Guid? versionId, CancellationToken ct)
    {
        if (versionId is { } pinned)
        {
            var version = await versions.GetAsync(pinned, ct);
            return version is { Status: VersionStatus.Active } && version.MatrixType == type
                ? version
                : Error.NotFound("config_version_not_found", $"No active {type.ToKey()} version {pinned}.");
        }

        var instant = at ?? clock.GetUtcNow();
        var inForce = await versions.FindActiveAtAsync(type, instant, ct);
        return inForce is null
            ? Error.NotFound("no_active_version", $"No {type.ToKey()} rules were in force at {instant:O}.")
            : inForce;
    }
}
