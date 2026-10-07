using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Queries.GetResume;

/// <summary>The candidate's CV for download. The caller disposes <see cref="ResumeContent.Content"/>.</summary>
public sealed record GetResumeQuery(Guid CandidateId) : IQuery<ResumeContent>;

public sealed record ResumeContent(ResumeDto Metadata, Stream Content);

internal sealed class GetResumeQueryHandler(
    ICandidateRepository candidates,
    IResumeStore store,
    IPersonalDataAccessLog accessLog) : IQueryHandler<GetResumeQuery, ResumeContent>
{
    public async Task<Result<ResumeContent>> Handle(GetResumeQuery query, CancellationToken ct)
    {
        var candidate = await candidates.GetAsync(query.CandidateId, ct);
        if (candidate?.Resume is not { } resume)
        {
            return Error.NotFound("resume_not_found", "This candidate has no CV on file.");
        }

        var content = await store.OpenAsync(resume.StorageKey, ct);
        if (content is null)
        {
            return Error.NotFound("resume_not_found", "This candidate has no CV on file.");
        }

        accessLog.Read([candidate.Id], "candidate.resume");
        return new ResumeContent(ResumeDto.From(resume), content);
    }
}
