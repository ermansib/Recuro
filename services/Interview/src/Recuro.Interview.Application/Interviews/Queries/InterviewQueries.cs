using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Application.Interviews.Queries;

/// <summary>
/// Who may read interview data: HR staff see every round; anyone else only the rounds they sit on
/// (RCU-INT-002: other people's forms are read-only, and strangers see nothing).
/// </summary>
internal static class Readers
{
    private static readonly string[] HrStaff = ["hrta", "hrhead", "mdceo"];

    public static bool IsHrStaff(ICurrentUser user) => HrStaff.Any(user.IsInRole);

    public static bool MayRead(ICurrentUser user, InterviewRound round) =>
        IsHrStaff(user) || round.Assessments.Any(a => a.InterviewerId == user.UserId);

    public static Error Denied() => Error.Forbidden("not_on_panel", "Only HR staff and the round's panel can see this interview.");
}

/// <summary>
/// The frontend's <c>getInterview(appId)</c>: the form for the application's latest round. A panel member
/// gets their own form; HR staff get the first one still waiting for feedback.
/// </summary>
public sealed record GetCurrentRoundQuery(string AppId) : IQuery<VersionedRound>;

internal sealed class GetCurrentRoundQueryHandler(IInterviewRepository interviews, ICurrentUser user) : IQueryHandler<GetCurrentRoundQuery, VersionedRound>
{
    public async Task<Result<VersionedRound>> Handle(GetCurrentRoundQuery query, CancellationToken ct)
    {
        var rounds = await interviews.ListForApplicationAsync(query.AppId, ct);
        var round = rounds.Where(r => r.Status != InterviewStatus.Cancelled).MaxBy(r => r.RoundNumber);
        if (round is null)
        {
            return InterviewErrors.NoInterview(query.AppId);
        }

        if (!Readers.MayRead(user, round))
        {
            return Readers.Denied();
        }

        var assessment = round.Assessments.FirstOrDefault(a => a.InterviewerId == user.UserId)
            ?? round.Assessments.FirstOrDefault(a => !a.IsSubmitted)
            ?? round.Assessments[0];
        return new VersionedRound(InterviewRoundDto.From(round, assessment, rounds), assessment.Version);
    }
}

/// <summary>One form by assessment id, with its ETag.</summary>
public sealed record GetAssessmentQuery(Guid AssessmentId) : IQuery<VersionedRound>;

internal sealed class GetAssessmentQueryHandler(IInterviewRepository interviews, ICurrentUser user) : IQueryHandler<GetAssessmentQuery, VersionedRound>
{
    public async Task<Result<VersionedRound>> Handle(GetAssessmentQuery query, CancellationToken ct)
    {
        var round = await interviews.GetByAssessmentAsync(query.AssessmentId, ct);
        if (round is null)
        {
            return InterviewErrors.AssessmentNotFound(query.AssessmentId);
        }

        if (!Readers.MayRead(user, round))
        {
            return Readers.Denied();
        }

        var assessment = round.Find(query.AssessmentId).Value;
        var rounds = await interviews.ListForApplicationAsync(round.AppId, ct);
        return new VersionedRound(InterviewRoundDto.From(round, assessment, rounds), assessment.Version);
    }
}

/// <summary>Every round of an application, oldest first (HR staff).</summary>
public sealed record ListApplicationInterviewsQuery(string AppId) : IQuery<IReadOnlyList<InterviewDto>>;

internal sealed class ListApplicationInterviewsQueryHandler(IInterviewRepository interviews)
    : IQueryHandler<ListApplicationInterviewsQuery, IReadOnlyList<InterviewDto>>
{
    public async Task<Result<IReadOnlyList<InterviewDto>>> Handle(ListApplicationInterviewsQuery query, CancellationToken ct)
    {
        var rounds = await interviews.ListForApplicationAsync(query.AppId, ct);
        return rounds.Select(InterviewDto.From).ToList();
    }
}

/// <summary>One round with its panel.</summary>
public sealed record GetInterviewQuery(Guid InterviewId) : IQuery<InterviewDto>;

internal sealed class GetInterviewQueryHandler(IInterviewRepository interviews, ICurrentUser user) : IQueryHandler<GetInterviewQuery, InterviewDto>
{
    public async Task<Result<InterviewDto>> Handle(GetInterviewQuery query, CancellationToken ct)
    {
        var round = await interviews.GetAsync(query.InterviewId, ct);
        if (round is null)
        {
            return InterviewErrors.NotFound(query.InterviewId);
        }

        return Readers.MayRead(user, round) ? InterviewDto.From(round) : Readers.Denied();
    }
}
