namespace Recuro.Candidate.Domain.Candidates;

public enum ApplicationOutcome
{
    Active,
    Unsuccessful,
    Hired,
}

/// <summary>Where the candidate stands overall, for retention (FRD §14).</summary>
public enum RetentionStatus
{
    /// <summary>No application yet.</summary>
    None,

    /// <summary>At least one application is still in progress.</summary>
    Active,

    /// <summary>Every application ended unsuccessfully; personal data is purged after the retention period.</summary>
    Unsuccessful,

    /// <summary>Hired: the record moves to employee retention rules and is never purged here.</summary>
    Hired,
}

/// <summary>
/// What the candidate service knows about one of the candidate's applications. The Pipeline service owns
/// applications; this copy is built from its events and only drives retention.
/// </summary>
public sealed class CandidateApplication
{
    private CandidateApplication()
    {
    }

    internal CandidateApplication(string appId) => AppId = appId;

    public string AppId { get; private set; } = string.Empty;

    public ApplicationOutcome Outcome { get; private set; } = ApplicationOutcome.Active;

    /// <summary>Set when the application ends unsuccessfully: personal data may be kept until then.</summary>
    public DateOnly? RetainUntil { get; private set; }

    internal void Close(ApplicationOutcome outcome, DateOnly? retainUntil)
    {
        Outcome = outcome;
        RetainUntil = outcome == ApplicationOutcome.Unsuccessful ? retainUntil : null;
    }
}
