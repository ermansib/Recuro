namespace Recuro.Gateway.Bff;

/// <summary>Bound from <c>Bff:Candidates</c>: where the two steps of "log candidate" go.</summary>
internal sealed class LogCandidateOptions
{
    public const string SectionName = "Bff:Candidates";

    /// <summary>Candidate service create endpoint (<c>POST /api/v1/candidates</c>).</summary>
    public Uri CandidatesUrl { get; set; } = new("http://localhost:5107/api/v1/candidates");

    /// <summary>Pipeline service create endpoint (<c>POST /api/v1/pipeline/applications</c>).</summary>
    public Uri ApplicationsUrl { get; set; } = new("http://localhost:5108/api/v1/pipeline/applications");

    /// <summary>Version of the privacy notice HR-TA confirms the candidate accepted.</summary>
    public string PrivacyConsentVersion { get; set; } = "v1";

    /// <summary>Budget for each step.</summary>
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
