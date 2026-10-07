namespace Recuro.Candidate.Domain.Candidates;

public enum ConsentType
{
    DataPrivacy,
    ConflictOfInterest,
}

/// <summary>
/// RCU-CND-001: one consent the candidate gave, with the exact wording version and where it was given.
/// </summary>
/// <param name="Type">What was consented to.</param>
/// <param name="TextVersion">Version of the consent text shown, e.g. <c>v1</c>.</param>
/// <param name="At">When it was given (UTC).</param>
/// <param name="Source">Where it was captured, e.g. <c>careers-portal</c> or <c>hr-logged</c>.</param>
public sealed record Consent(ConsentType Type, string TextVersion, DateTimeOffset At, string Source);

/// <summary>Which consents a candidate must have given before Recuro may process their data (FRD §14).</summary>
public static class ConsentPolicy
{
    private static readonly ConsentType[] AlwaysRequired = [ConsentType.DataPrivacy];

    /// <summary>The consents required for a candidate from <paramref name="source"/>.</summary>
    public static IReadOnlyList<ConsentType> RequiredFor(CandidateSource source) =>
        source switch
        {
            // Internal and external candidates alike need the privacy notice; nothing else is mandatory at intake.
            _ => AlwaysRequired,
        };

    public static IReadOnlyList<ConsentType> Missing(CandidateSource source, IEnumerable<Consent> given)
    {
        var types = given.Select(c => c.Type).ToHashSet();
        return RequiredFor(source).Where(required => !types.Contains(required)).ToList();
    }
}
