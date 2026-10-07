using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Commands;

/// <summary>
/// Gives the current tenant the FRD §5 seed as version 1 of every matrix it doesn't have yet. Safe to
/// run again: matrices that already have a version are left alone. Returns the types it seeded.
/// </summary>
public sealed record SeedDefaultRuleSetsCommand : ICommand<IReadOnlyList<string>>;

internal sealed class SeedDefaultRuleSetsCommandHandler(IRuleSetVersions versions, TimeProvider clock)
    : ICommandHandler<SeedDefaultRuleSetsCommand, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(SeedDefaultRuleSetsCommand command, CancellationToken ct)
    {
        var seeded = new List<string>();
        foreach (var (type, matrix) in DefaultRuleSets.All)
        {
            var content = MatrixJson.Write(matrix);
            var added = await versions.AddNextAsync(
                type,
                number => number == 1 ? RuleSetVersion.Seed(type, content, DefaultRuleSets.EffectiveFrom, clock.GetUtcNow()) : null,
                ct);
            if (added is not null)
            {
                seeded.Add(type.ToKey());
            }
        }

        return seeded;
    }
}
