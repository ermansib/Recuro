using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Candidate.Application.Abstractions;

namespace Recuro.Candidate.Application.Candidates.Masking;

/// <summary>Masks candidates for the current caller, from the Identity maps of each of their roles.</summary>
public interface ICallerMask
{
    Task<Func<CandidateDto, CandidateDto>> ForCallerAsync(CancellationToken ct);
}

internal sealed class CallerMask(ICurrentUser caller, IMaskingMaps maps, IMaskHasher hasher) : ICallerMask
{
    public async Task<Func<CandidateDto, CandidateDto>> ForCallerAsync(CancellationToken ct)
    {
        var roleMaps = new List<IReadOnlyDictionary<string, MaskStrategy>?>();
        foreach (var role in caller.Roles.Distinct(StringComparer.Ordinal))
        {
            roleMaps.Add(await maps.GetAsync(role, ct));
        }

        var mask = CandidateMasking.Combine(roleMaps);
        return candidate => CandidateMasking.Apply(candidate, mask, hasher.Hash);
    }
}
