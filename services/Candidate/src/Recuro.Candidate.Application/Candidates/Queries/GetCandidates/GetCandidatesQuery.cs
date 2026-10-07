using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Application.Candidates.Masking;

namespace Recuro.Candidate.Application.Candidates.Queries.GetCandidates;

/// <summary>
/// Several candidates in one call, masked for the caller's role. The Pipeline board uses it to build its
/// cards without one request per candidate. Unknown ids are left out.
/// </summary>
public sealed record GetCandidatesQuery(IReadOnlyList<Guid> Ids) : IQuery<IReadOnlyList<CandidateDto>>;

internal sealed class GetCandidatesQueryValidator : AbstractValidator<GetCandidatesQuery>
{
    public GetCandidatesQueryValidator()
    {
        RuleFor(q => q.Ids).NotEmpty().Must(ids => ids.Count <= CandidateLimits.MaxBatch)
            .WithMessage($"Ask for at most {CandidateLimits.MaxBatch} candidates at a time.");
    }
}

internal sealed class GetCandidatesQueryHandler(
    ICandidateRepository candidates,
    ICallerMask callerMask,
    IPersonalDataAccessLog accessLog) : IQueryHandler<GetCandidatesQuery, IReadOnlyList<CandidateDto>>
{
    public async Task<Result<IReadOnlyList<CandidateDto>>> Handle(GetCandidatesQuery query, CancellationToken ct)
    {
        var found = await candidates.GetManyAsync(query.Ids.Distinct().ToList(), ct);
        if (found.Count > 0)
        {
            accessLog.Read(found.Select(c => c.Id).ToList(), "candidate.batch");
        }

        var mask = await callerMask.ForCallerAsync(ct);
        return Result.Success<IReadOnlyList<CandidateDto>>(found.Select(c => mask(CandidateDto.From(c))).ToList());
    }
}
