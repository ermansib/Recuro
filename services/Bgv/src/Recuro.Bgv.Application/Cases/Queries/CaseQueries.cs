using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Cases.Queries;

/// <summary>The case for an application (frontend <c>getBgvCase(appId)</c>), or by case id.</summary>
public sealed record GetCaseQuery(string CaseRef) : IQuery<BgvCaseDto>;

internal sealed class GetCaseQueryHandler(IBgvCaseRepository cases, ISensitiveNotePolicy sensitiveNotes, TimeProvider clock)
    : IQueryHandler<GetCaseQuery, BgvCaseDto>
{
    public async Task<Result<BgvCaseDto>> Handle(GetCaseQuery query, CancellationToken ct)
    {
        var bgvCase = await cases.FindAsync(query.CaseRef, ct);
        return bgvCase is null
            ? BgvErrors.NotFound(query.CaseRef)
            : BgvCaseDto.From(bgvCase, clock.GetUtcNow(), await sensitiveNotes.CallerMaySeeAsync(ct));
    }
}

/// <summary>RCU-BGV-007: <c>{ cleared, blockers }</c> for Offer (OFR-007) and Onboarding.</summary>
public sealed record GetReleaseGateQuery(string CaseRef) : IQuery<ReleaseGateDto>;

internal sealed class GetReleaseGateQueryHandler(IBgvCaseRepository cases) : IQueryHandler<GetReleaseGateQuery, ReleaseGateDto>
{
    public async Task<Result<ReleaseGateDto>> Handle(GetReleaseGateQuery query, CancellationToken ct)
    {
        var bgvCase = await cases.FindAsync(query.CaseRef, ct);
        return bgvCase is null ? BgvErrors.NotFound(query.CaseRef) : ReleaseGateDto.From(bgvCase);
    }
}

/// <summary>RCU-VND-003: open cases whose vendor was de-empanelled.</summary>
public sealed record ListReassignmentQueueQuery : IQuery<IReadOnlyList<BgvCaseDto>>;

internal sealed class ListReassignmentQueueQueryHandler(IBgvCaseRepository cases, ISensitiveNotePolicy sensitiveNotes, TimeProvider clock)
    : IQueryHandler<ListReassignmentQueueQuery, IReadOnlyList<BgvCaseDto>>
{
    public async Task<Result<IReadOnlyList<BgvCaseDto>>> Handle(ListReassignmentQueueQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var show = await sensitiveNotes.CallerMaySeeAsync(ct);
        var waiting = await cases.ListNeedingReassignmentAsync(ct);
        return waiting.Select(c => BgvCaseDto.From(c, now, show)).ToList();
    }
}
