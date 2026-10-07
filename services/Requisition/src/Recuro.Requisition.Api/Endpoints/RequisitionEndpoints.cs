using Microsoft.AspNetCore.Mvc;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Requisition.Api.Http;
using Recuro.Requisition.Application.JobDescriptions;
using Recuro.Requisition.Application.Requisitions;
using Recuro.Requisition.Application.Requisitions.Commands;
using Recuro.Requisition.Application.Requisitions.Queries;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Api.Endpoints;

/// <summary>Requisition (MRF) and job description API. Shapes are the frontend's (frontend/src/api/contract.ts).</summary>
internal static class RequisitionEndpoints
{
    public static IEndpointRouteBuilder MapRequisitionEndpoints(this IEndpointRouteBuilder app)
    {
        var requisitions = app.MapGroup("/api/v1/requisitions").WithTags("Requisitions");

        requisitions.MapGet("/", ListAsync)
            .RequireAuthorization(RequisitionPolicies.Read)
            .WithSummary("RCU-REQ-007: the requisition tracker, newest first (frontend listRequisitions).");

        requisitions.MapGet("/{reqId}", GetAsync)
            .RequireAuthorization(RequisitionPolicies.Read)
            .WithSummary("One requisition. The ETag is the version to send back in If-Match.");

        requisitions.MapPost("/", CreateAsync)
            .RequireAuthorization(RequisitionPolicies.Raise)
            .WithSummary("RCU-REQ-001/002: raise an MRF and submit it for approval (frontend createRequisition); ?draft=true only saves it.");

        requisitions.MapPatch("/{reqId}", UpdateDraftAsync)
            .RequireAuthorization(RequisitionPolicies.Raise)
            .WithSummary("RCU-REQ-001: autosave a draft. Send If-Match with the ETag; a stale version returns 409.");

        requisitions.MapPost("/{reqId}/submit", SubmitAsync)
            .RequireAuthorization(RequisitionPolicies.Raise)
            .WithSummary("RCU-REQ-002: submit a draft. Resolves the DOA route in Config and opens the approval workflow.");

        requisitions.MapPost("/{reqId}/cancel", CancelAsync)
            .RequireAuthorization(RequisitionPolicies.Cancel)
            .WithSummary("RCU-REQ-008: cancel with a documented reason (HR Head).");

        requisitions.MapGet("/{reqId}/sourcing-gate", SourcingGateAsync)
            .RequireAuthorization(RequisitionPolicies.Read)
            .WithSummary("RCU-REQ-005: allowed is true only once the MRF is approved (Approved..Offer).");

        requisitions.MapGet("/{reqId}/job-description", GetJobDescriptionAsync)
            .RequireAuthorization(RequisitionPolicies.ReadJd)
            .WithSummary("S-05: the requisition's job description (frontend getJobDescription).");

        app.MapGroup("/api/v1/job-descriptions").WithTags("Job descriptions")
            .MapPost("/{id:guid}/submit", SubmitJobDescriptionAsync)
            .RequireAuthorization(RequisitionPolicies.EditJd)
            .WithSummary("RCU-JD-001..004: validate and freeze the next JD version (frontend submitJobDescription).");

        return app;
    }

    private static async Task<IResult> ListAsync(
        RequisitionState? state,
        string? grade,
        string? location,
        string? q,
        int? limit,
        IQueryHandler<ListRequisitionsQuery, IReadOnlyList<RequisitionDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListRequisitionsQuery(state, grade, location, q, limit), ct)).ToHttpResult();

    private static async Task<IResult> GetAsync(
        string reqId,
        HttpContext http,
        IQueryHandler<GetRequisitionQuery, VersionedRequisition> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new GetRequisitionQuery(reqId), ct));

    private static async Task<IResult> CreateAsync(
        RequisitionInput input,
        bool? draft,
        HttpContext http,
        ICommandHandler<CreateRequisitionCommand, VersionedRequisition> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new CreateRequisitionCommand(input, draft == true), ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        ETags.Write(http, result.Value.Version);
        return Results.Created($"/api/v1/requisitions/{Uri.EscapeDataString(result.Value.Requisition.ReqId)}", result.Value.Requisition);
    }

    private static async Task<IResult> UpdateDraftAsync(
        string reqId,
        RequisitionInput input,
        HttpContext http,
        ICommandHandler<UpdateRequisitionDraftCommand, VersionedRequisition> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new UpdateRequisitionDraftCommand(reqId, ETags.ReadIfMatch(http), input), ct));

    private static async Task<IResult> SubmitAsync(
        string reqId,
        HttpContext http,
        ICommandHandler<SubmitRequisitionCommand, VersionedRequisition> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new SubmitRequisitionCommand(reqId, ETags.ReadIfMatch(http)), ct));

    private static async Task<IResult> CancelAsync(
        string reqId,
        ReasonRequest request,
        HttpContext http,
        ICommandHandler<CancelRequisitionCommand, VersionedRequisition> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new CancelRequisitionCommand(reqId, request.Reason ?? string.Empty), ct));

    private static async Task<IResult> SourcingGateAsync(
        string reqId,
        IQueryHandler<GetSourcingGateQuery, SourcingGateDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetSourcingGateQuery(reqId), ct)).ToHttpResult();

    private static async Task<IResult> GetJobDescriptionAsync(
        string reqId,
        IQueryHandler<GetJobDescriptionQuery, JobDescriptionDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetJobDescriptionQuery(reqId), ct)).ToHttpResult();

    private static async Task<IResult> SubmitJobDescriptionAsync(
        Guid id,
        [FromBody] JobDescriptionInput input,
        ICommandHandler<SubmitJobDescriptionCommand, JobDescriptionDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SubmitJobDescriptionCommand(id, input), ct)).ToHttpResult();

    private static IResult Versioned(HttpContext http, Result<VersionedRequisition> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        ETags.Write(http, result.Value.Version);
        return Results.Ok(result.Value.Requisition);
    }
}

/// <summary>Body of cancel: <c>{ "reason": "…" }</c>.</summary>
internal sealed record ReasonRequest(string? Reason);
