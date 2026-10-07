using Recuro.Pipeline.Application.Abstractions;
using ApplicationEntity = Recuro.Pipeline.Domain.Applications.Application;

namespace Recuro.Pipeline.Application.Applications.Queries.ListPipelineCards;

/// <summary>Joins applications with their candidates from the Candidate service, in one batched call.</summary>
internal static class PipelineCards
{
    public static async Task<IReadOnlyList<PipelineCardDto>> BuildAsync(
        IReadOnlyList<ApplicationEntity> applications,
        ICandidateDirectory candidates,
        CancellationToken ct)
    {
        if (applications.Count == 0)
        {
            return [];
        }

        var found = await candidates.GetManyAsync(applications.Select(a => a.CandidateId).Distinct().ToList(), ct);

        // A candidate the Candidate service no longer returns has no card; the column counts still include it.
        return applications
            .Where(a => found.ContainsKey(a.CandidateId))
            .Select(a => new PipelineCardDto(ApplicationDto.From(a), found[a.CandidateId]))
            .ToList();
    }
}
