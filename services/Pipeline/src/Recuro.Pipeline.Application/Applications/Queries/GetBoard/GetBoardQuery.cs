using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Application.Applications.Queries.ListPipelineCards;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Application.Applications.Queries.GetBoard;

/// <summary>RCU-PPL-006: columns, cards and counts for one requisition in one payload.</summary>
public sealed record GetBoardQuery(string ReqId) : IQuery<PipelineBoardDto>;

internal sealed class GetBoardQueryHandler(IApplicationRepository applications, ICandidateDirectory candidates)
    : IQueryHandler<GetBoardQuery, PipelineBoardDto>
{
    public async Task<Result<PipelineBoardDto>> Handle(GetBoardQuery query, CancellationToken ct)
    {
        var all = await applications.ListByRequisitionAsync(query.ReqId, ct);
        var onBoard = all.Where(a => ApplicationTransitions.BoardColumns.Contains(a.Stage)).ToList();
        var cards = await PipelineCards.BuildAsync(onBoard, candidates, ct);

        var columns = ApplicationTransitions.BoardColumns
            .Select(stage => new BoardColumnDto(
                stage.ToString(),
                onBoard.Count(a => a.Stage == stage),
                cards.Where(c => c.Application.Stage == stage.ToString()).ToList()))
            .ToList();
        var offBoard = all
            .Where(a => !ApplicationTransitions.BoardColumns.Contains(a.Stage))
            .GroupBy(a => a.Stage.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new PipelineBoardDto(query.ReqId, all.Count, columns, offBoard);
    }
}
