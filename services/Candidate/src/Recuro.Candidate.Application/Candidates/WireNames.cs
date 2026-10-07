using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates;

/// <summary>
/// The frontend's spellings of <see cref="CandidateSource"/> (frontend/src/domain/types.ts), e.g.
/// <c>IJP</c> and <c>Walk-in</c>. Kept here so the domain enum stays free of wire concerns.
/// </summary>
public static class CandidateSourceNames
{
    private static readonly Dictionary<CandidateSource, string> Names = new()
    {
        [CandidateSource.Ijp] = "IJP",
        [CandidateSource.Referral] = "Referral",
        [CandidateSource.Portal] = "Portal",
        [CandidateSource.Campus] = "Campus",
        [CandidateSource.WalkIn] = "Walk-in",
        [CandidateSource.Consultant] = "Consultant",
        [CandidateSource.Social] = "Social",
        [CandidateSource.LinkedIn] = "LinkedIn",
        [CandidateSource.Direct] = "Direct",
    };

    private static readonly Dictionary<string, CandidateSource> Sources =
        Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> All => Names.Values;

    public static string ToName(CandidateSource source) => Names[source];

    public static bool TryParse(string? name, out CandidateSource source) =>
        Sources.TryGetValue(name ?? string.Empty, out source);
}
