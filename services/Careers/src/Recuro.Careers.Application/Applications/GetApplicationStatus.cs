using System.Text.RegularExpressions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Domain.Applications;

namespace Recuro.Careers.Application.Applications;

/// <summary>
/// RCU-CAR-005: what an applicant sees for an APP-ID. <c>stage</c> is the coarse key
/// (Received, UnderReview, Interview, Offer, Decision); <c>status</c> is the sentence the frontend mock's
/// <c>getApplicationStatus</c> returns for it. No PII, no interviewer detail.
/// </summary>
public sealed record ApplicationStatusDto(string AppId, string Stage, string Status);

public sealed record GetApplicationStatusQuery(string AppId) : IQuery<ApplicationStatusDto>;

internal sealed partial class GetApplicationStatusQueryHandler(IPublicApplicationRepository applications)
    : IQueryHandler<GetApplicationStatusQuery, ApplicationStatusDto>
{
    public async Task<Result<ApplicationStatusDto>> Handle(GetApplicationStatusQuery query, CancellationToken ct)
    {
        // A malformed, unknown or other tenant's id all get the same neutral answer (no enumeration).
        var appId = (query.AppId ?? string.Empty).Trim().ToUpperInvariant();
        if (!AppIdPattern().IsMatch(appId))
        {
            return ApplicationErrors.StatusNotFound;
        }

        var application = await applications.GetByAppIdAsync(appId, ct);
        if (application is not { State: IntakeState.Completed })
        {
            return ApplicationErrors.StatusNotFound;
        }

        return new ApplicationStatusDto(appId, application.PublicStage.ToString(), StatusText.For(application.PipelineStage));
    }

    [GeneratedRegex(@"^APP-\d{4}-\d{4,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdPattern();
}

/// <summary>The applicant-facing sentence per Pipeline stage, the same text as the frontend mock (<c>coarseStatus</c>).</summary>
public static class StatusText
{
    private const string UnderReview = "Under HR review — screening call within ≤7 days";

    private static readonly Dictionary<string, string> ByStage = new(StringComparer.Ordinal)
    {
        ["Sourced"] = UnderReview,
        ["Screened"] = "Interviews in progress",
        ["Interview"] = "Interviews in progress",
        ["Selection"] = "Interviews in progress",
        ["BGV"] = "Final stages — our team will be in touch",
        ["Offer"] = "Final stages — our team will be in touch",
        ["PreBoarding"] = "Final stages — our team will be in touch",
        ["Onboarded"] = "Welcome aboard",
        ["Confirmed"] = "Welcome aboard",
        ["Hold"] = "Under review",
        ["Rejected"] = "Closed — thank you for your interest",
        ["Withdrawn"] = "Closed — thank you for your interest",
    };

    public static string For(string? pipelineStage) =>
        pipelineStage is not null && ByStage.TryGetValue(pipelineStage, out var text) ? text : UnderReview;
}
