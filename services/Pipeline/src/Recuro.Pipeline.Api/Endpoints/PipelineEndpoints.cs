using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Pipeline.Application.Applications;
using Recuro.Pipeline.Application.Applications.Commands.CreateApplication;
using Recuro.Pipeline.Application.Applications.Commands.MoveApplication;
using Recuro.Pipeline.Application.Applications.Commands.RejectApplication;
using Recuro.Pipeline.Application.Applications.Queries.GetApplication;
using Recuro.Pipeline.Application.Applications.Queries.GetBoard;
using Recuro.Pipeline.Application.Applications.Queries.ListPipelineCards;

namespace Recuro.Pipeline.Api.Endpoints;

/// <summary>Pipeline API (RCU-PPL-001..006). Responses use the frontend's <c>Application</c> and <c>PipelineCard</c> shapes.</summary>
internal static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pipeline").WithTags("Pipeline");

        group.MapGet("/requisitions/{reqId}/cards", ListCardsAsync)
            .RequireAuthorization(PipelinePolicies.Read)
            .WithSummary("Every application of a requisition with its candidate (frontend listPipeline).");

        group.MapGet("/requisitions/{reqId}/board", GetBoardAsync)
            .RequireAuthorization(PipelinePolicies.Read)
            .WithSummary("RCU-PPL-006: kanban columns, cards and counts in one payload.");

        group.MapPost("/applications", CreateAsync)
            .RequireAuthorization(PipelinePolicies.Create)
            .WithSummary("RCU-PPL-001: create an application at Sourced. 409 sourcing_locked until the requisition is approved.");

        group.MapGet("/applications/{appId}", GetAsync)
            .RequireAuthorization(PipelinePolicies.Read)
            .WithSummary("One application.");

        group.MapPost("/applications/{appId}/move", MoveAsync)
            .RequireAuthorization(PipelinePolicies.Move)
            .WithSummary("RCU-PPL-002: move to another stage (frontend moveApplication). 409 lists the allowed stages.");

        group.MapPost("/applications/{appId}/reject", RejectAsync)
            .RequireAuthorization(PipelinePolicies.Move)
            .WithSummary("RCU-PPL-003: final rejection with a mandatory reason (frontend rejectApplication).");

        return app;
    }

    private static async Task<IResult> ListCardsAsync(
        string reqId,
        IQueryHandler<ListPipelineCardsQuery, IReadOnlyList<PipelineCardDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListPipelineCardsQuery(reqId), ct)).ToHttpResult();

    private static async Task<IResult> GetBoardAsync(string reqId, IQueryHandler<GetBoardQuery, PipelineBoardDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetBoardQuery(reqId), ct)).ToHttpResult();

    private static async Task<IResult> CreateAsync(
        CreateApplicationRequest request,
        ICommandHandler<CreateApplicationCommand, ApplicationDto> handler,
        CancellationToken ct)
    {
        var command = new CreateApplicationCommand(request.ReqId ?? string.Empty, request.CandidateId ?? string.Empty, request.Source ?? string.Empty, request.Note);
        return (await handler.Handle(command, ct)).ToCreatedResult(dto => $"/api/v1/pipeline/applications/{dto.AppId}");
    }

    private static async Task<IResult> GetAsync(string appId, IQueryHandler<GetApplicationQuery, ApplicationDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetApplicationQuery(appId), ct)).ToHttpResult();

    private static async Task<IResult> MoveAsync(
        string appId,
        MoveApplicationRequest request,
        ICommandHandler<MoveApplicationCommand, ApplicationDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new MoveApplicationCommand(appId, request.To ?? string.Empty), ct)).ToHttpResult();

    private static async Task<IResult> RejectAsync(
        string appId,
        RejectApplicationRequest request,
        ICommandHandler<RejectApplicationCommand, ApplicationDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new RejectApplicationCommand(appId, request.Reason ?? string.Empty), ct)).ToHttpResult();
}

/// <summary>Body of <c>POST /api/v1/pipeline/applications</c>.</summary>
internal sealed record CreateApplicationRequest(string? ReqId, string? CandidateId, string? Source, string? Note);

/// <summary>Body of <c>POST /api/v1/pipeline/applications/{appId}/move</c>.</summary>
internal sealed record MoveApplicationRequest(string? To);

/// <summary>Body of <c>POST /api/v1/pipeline/applications/{appId}/reject</c>.</summary>
internal sealed record RejectApplicationRequest(string? Reason);
