using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Application.Candidates.Masking;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Queries.GetCandidate;

/// <summary>RCU-CND-004: one candidate, masked for the caller's role (frontend <c>getCandidate</c>).</summary>
public sealed record GetCandidateQuery(Guid Id) : IQuery<CandidateDto>;

internal sealed class GetCandidateQueryHandler(
    ICandidateRepository candidates,
    ICallerMask callerMask,
    IPersonalDataAccessLog accessLog) : IQueryHandler<GetCandidateQuery, CandidateDto>
{
    public async Task<Result<CandidateDto>> Handle(GetCandidateQuery query, CancellationToken ct)
    {
        var candidate = await candidates.GetAsync(query.Id, ct);
        if (candidate is null)
        {
            return CandidateErrors.NotFound(query.Id);
        }

        accessLog.Read([candidate.Id], "candidate.get");
        var mask = await callerMask.ForCallerAsync(ct);
        return mask(CandidateDto.From(candidate));
    }
}
