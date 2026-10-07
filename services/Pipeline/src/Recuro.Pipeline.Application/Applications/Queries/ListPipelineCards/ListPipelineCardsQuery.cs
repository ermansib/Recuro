using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;

namespace Recuro.Pipeline.Application.Applications.Queries.ListPipelineCards;

/// <summary>Every application of a requisition with its candidate (frontend <c>listPipeline</c>).</summary>
public sealed record ListPipelineCardsQuery(string ReqId) : IQuery<IReadOnlyList<PipelineCardDto>>;

internal sealed class ListPipelineCardsQueryHandler(IApplicationRepository applications, ICandidateDirectory candidates)
    : IQueryHandler<ListPipelineCardsQuery, IReadOnlyList<PipelineCardDto>>
{
    public async Task<Result<IReadOnlyList<PipelineCardDto>>> Handle(ListPipelineCardsQuery query, CancellationToken ct)
    {
        var list = await applications.ListByRequisitionAsync(query.ReqId, ct);
        return Result.Success(await PipelineCards.BuildAsync(list, candidates, ct));
    }
}
