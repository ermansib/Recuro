using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Interview.Api.Http;
using Recuro.Interview.Application.Interviews;
using Recuro.Interview.Application.Interviews.Commands;
using Recuro.Interview.Application.Interviews.Queries;
using Recuro.Interview.Application.Selection;

namespace Recuro.Interview.Api.Endpoints;

/// <summary>Interview scheduling and assessment API (S-09). Form shapes are the frontend's <c>InterviewRound</c>.</summary>
internal static class InterviewEndpoints
{
    public static IEndpointRouteBuilder MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var interviews = app.MapGroup("/api/v1/interviews").WithTags("Interviews");

        interviews.MapPost("/", ScheduleAsync)
            .RequireAuthorization(InterviewPolicies.Schedule)
            .WithSummary("RCU-INT-001: schedule a round from the grade's template. Competencies come from the requisition's JD.");

        interviews.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization()
            .WithSummary("One round with its panel. HR staff and the round's panel only.");

        interviews.MapPost("/{id:guid}/reschedule", RescheduleAsync)
            .RequireAuthorization(InterviewPolicies.Schedule)
            .WithSummary("RCU-INT-007: move a round with a reason; the panel gets a new invite. Not once feedback is in.");

        interviews.MapGet("/applications/{appId}", ListForApplicationAsync)
            .RequireAuthorization(InterviewPolicies.Read)
            .WithSummary("Every round of an application, oldest first.");

        interviews.MapGet("/applications/{appId}/current", CurrentAsync)
            .RequireAuthorization()
            .WithSummary("The frontend's getInterview(appId): the caller's form for the latest round. The ETag is the version for If-Match.");

        interviews.MapGet("/assessments/{id:guid}", GetAssessmentAsync)
            .RequireAuthorization()
            .WithSummary("One assessment form (frontend InterviewRound).");

        interviews.MapPut("/assessments/{id:guid}", SaveDraftAsync)
            .RequireAuthorization()
            .WithSummary("RCU-INT-002: autosave the caller's own form. Send If-Match; a stale version returns 409.");

        interviews.MapPost("/assessments/{id:guid}/submit", SubmitAsync)
            .RequireAuthorization()
            .WithSummary("RCU-INT-002/003: submit the form; it locks. Every competency rated or N/A, justification required.");

        interviews.MapPost("/assessments/{id:guid}/supersede", SupersedeAsync)
            .RequireAuthorization()
            .WithSummary("RCU-INT-003: a new revision of a submitted form with a reason; earlier revisions stay in history.");

        interviews.MapGet("/applications/{appId}/selection-summary", SelectionSummaryAsync)
            .RequireAuthorization(InterviewPolicies.Read)
            .WithSummary("RCU-INT-005: averages per round and overall, recommendations and open flags.");

        interviews.MapGet("/applications/{appId}/selection-summary/pdf", SelectionSummaryPdfAsync)
            .RequireAuthorization(InterviewPolicies.Read)
            .WithSummary("RCU-INT-005: the selection summary as a PDF snapshot.");

        interviews.MapPost("/applications/{appId}/selection-summary/submit", SubmitSelectionAsync)
            .RequireAuthorization(InterviewPolicies.Select)
            .WithSummary("RCU-INT-006: submit the selection; senior grades go to ratification, others are ratified at once.");

        return app;
    }

    private static async Task<IResult> ScheduleAsync(
        ScheduleInterviewRequest request,
        ICommandHandler<ScheduleInterviewCommand, InterviewDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new ScheduleInterviewCommand(request), ct);
        return result.IsFailure ? result.Error!.ToProblem() : Results.Created($"/api/v1/interviews/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> GetAsync(Guid id, IQueryHandler<GetInterviewQuery, InterviewDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetInterviewQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> RescheduleAsync(
        Guid id,
        RescheduleRequest request,
        ICommandHandler<RescheduleInterviewCommand, InterviewDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new RescheduleInterviewCommand(id, request), ct)).ToHttpResult();

    private static async Task<IResult> ListForApplicationAsync(
        string appId,
        IQueryHandler<ListApplicationInterviewsQuery, IReadOnlyList<InterviewDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListApplicationInterviewsQuery(appId), ct)).ToHttpResult();

    private static async Task<IResult> CurrentAsync(
        string appId,
        HttpContext http,
        IQueryHandler<GetCurrentRoundQuery, VersionedRound> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new GetCurrentRoundQuery(appId), ct));

    private static async Task<IResult> GetAssessmentAsync(
        Guid id,
        HttpContext http,
        IQueryHandler<GetAssessmentQuery, VersionedRound> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new GetAssessmentQuery(id), ct));

    private static async Task<IResult> SaveDraftAsync(
        Guid id,
        AssessmentRequest request,
        HttpContext http,
        ICommandHandler<SaveAssessmentDraftCommand, VersionedRound> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new SaveAssessmentDraftCommand(id, ETags.ReadIfMatch(http), request), ct));

    private static async Task<IResult> SubmitAsync(
        Guid id,
        AssessmentRequest request,
        HttpContext http,
        ICommandHandler<SubmitAssessmentCommand, VersionedRound> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new SubmitAssessmentCommand(id, ETags.ReadIfMatch(http), request), ct));

    private static async Task<IResult> SupersedeAsync(
        Guid id,
        AssessmentRequest request,
        HttpContext http,
        ICommandHandler<SupersedeAssessmentCommand, VersionedRound> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new SupersedeAssessmentCommand(id, request), ct));

    private static async Task<IResult> SelectionSummaryAsync(
        string appId,
        IQueryHandler<GetSelectionSummaryQuery, SelectionSummaryDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetSelectionSummaryQuery(appId), ct)).ToHttpResult();

    private static async Task<IResult> SelectionSummaryPdfAsync(
        string appId,
        IQueryHandler<GetSelectionSummaryPdfQuery, byte[]> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new GetSelectionSummaryPdfQuery(appId), ct);
        return result.IsFailure
            ? result.Error!.ToProblem()
            : Results.File(result.Value, "application/pdf", $"selection-summary-{appId}.pdf");
    }

    private static async Task<IResult> SubmitSelectionAsync(
        string appId,
        ICommandHandler<SubmitSelectionCommand, SelectionSummaryDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SubmitSelectionCommand(appId), ct)).ToHttpResult();

    private static IResult Versioned(HttpContext http, Result<VersionedRound> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        ETags.Write(http, result.Value.Version);
        return Results.Ok(result.Value.Round);
    }
}
