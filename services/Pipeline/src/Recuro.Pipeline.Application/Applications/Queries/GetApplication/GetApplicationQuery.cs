using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Application.Applications.Queries.GetApplication;

public sealed record GetApplicationQuery(string AppId) : IQuery<ApplicationDto>;

internal sealed class GetApplicationQueryHandler(IApplicationRepository applications) : IQueryHandler<GetApplicationQuery, ApplicationDto>
{
    public async Task<Result<ApplicationDto>> Handle(GetApplicationQuery query, CancellationToken ct) =>
        await applications.GetAsync(query.AppId, ct) is { } application
            ? ApplicationDto.From(application)
            : ApplicationErrors.NotFound(query.AppId);
}
