using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Application.RuleSets.Commands;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Queries;

/// <summary>Every version of a matrix, newest first, with drafts and rejections (the change history).</summary>
public sealed record ListVersionsQuery(MatrixType MatrixType) : IQuery<IReadOnlyList<RuleSetVersionDto>>;

public sealed record GetVersionQuery(MatrixType MatrixType, Guid Id) : IQuery<RuleSetVersionDto>;

internal sealed class ListVersionsQueryHandler(IRuleSetVersions versions) : IQueryHandler<ListVersionsQuery, IReadOnlyList<RuleSetVersionDto>>
{
    public async Task<Result<IReadOnlyList<RuleSetVersionDto>>> Handle(ListVersionsQuery query, CancellationToken ct)
    {
        var all = await versions.ListAsync(query.MatrixType, ct);
        IReadOnlyList<RuleSetVersionDto> result = all.Select(v => RuleSetVersionDto.From(v, all)).ToList();
        return Result.Success(result);
    }
}

internal sealed class GetVersionQueryHandler(IRuleSetVersions versions) : IQueryHandler<GetVersionQuery, RuleSetVersionDto>
{
    public async Task<Result<RuleSetVersionDto>> Handle(GetVersionQuery query, CancellationToken ct)
    {
        var all = await versions.ListAsync(query.MatrixType, ct);
        var version = all.FirstOrDefault(v => v.Id == query.Id);
        return version is null ? RuleSetErrors.NotFound(query.MatrixType, query.Id) : RuleSetVersionDto.From(version, all);
    }
}
