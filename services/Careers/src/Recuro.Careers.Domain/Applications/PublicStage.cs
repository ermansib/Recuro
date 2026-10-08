namespace Recuro.Careers.Domain.Applications;

/// <summary>RCU-CAR-005: what an applicant may know about their application. No interviewer or HR detail.</summary>
public enum PublicStage
{
    Received,
    UnderReview,
    Interview,
    Offer,
    Decision,
}

/// <summary>Maps Pipeline stages (FRD §6.2) to the coarse public stage, as data.</summary>
public static class PublicStages
{
    private static readonly Dictionary<string, PublicStage> ByPipelineStage = new(StringComparer.Ordinal)
    {
        ["Sourced"] = PublicStage.Received,
        ["Screened"] = PublicStage.UnderReview,
        ["Hold"] = PublicStage.UnderReview,
        ["Interview"] = PublicStage.Interview,
        ["Selection"] = PublicStage.Interview,
        ["BGV"] = PublicStage.Offer,
        ["Offer"] = PublicStage.Offer,
        ["PreBoarding"] = PublicStage.Offer,
        ["Onboarded"] = PublicStage.Decision,
        ["Confirmed"] = PublicStage.Decision,
        ["Rejected"] = PublicStage.Decision,
        ["Withdrawn"] = PublicStage.Decision,
    };

    /// <summary>An unknown stage (a newer Pipeline) reads as "under review" rather than failing.</summary>
    public static PublicStage FromPipeline(string stage) =>
        ByPipelineStage.TryGetValue(stage, out var mapped) ? mapped : PublicStage.UnderReview;
}
