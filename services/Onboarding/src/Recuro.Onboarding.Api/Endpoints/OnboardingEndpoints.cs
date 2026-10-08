using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Onboarding.Application.Cases;
using Recuro.Onboarding.Application.Cases.Commands;
using Recuro.Onboarding.Application.Cases.Queries;

namespace Recuro.Onboarding.Api.Endpoints;

/// <summary>
/// Onboarding API (RCU-ONB-001..005), behind screen S-13. Responses use the frontend's
/// <c>OnboardingCase</c> shape. <c>{caseRef}</c> is the application id or the case id.
/// </summary>
internal static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/onboarding").WithTags("Onboarding");

        group.MapGet("/cases", ListAsync)
            .RequireAuthorization(OnboardingPolicies.Read)
            .WithSummary("Running cases (or ?status=PreBoarding|Day1Ready|Confirmed|Cancelled), soonest joining first.");

        group.MapGet("/cases/{caseRef}", GetAsync)
            .RequireAuthorization(OnboardingPolicies.Read)
            .WithSummary("RCU-ONB-001..005: the case for an application — checklist, documents, milestones, probation.");

        group.MapPut("/cases/{caseRef}/checklist/{itemKey}", SetChecklistItemAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("RCU-ONB-002: tick or untick a Day-1 item. 100% marks the record Day-1 Ready (then 409 checklist_locked).");

        group.MapPut("/cases/{caseRef}/assignments", AssignAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("The reporting manager (probation reminders) and the buddy.");

        group.MapPut("/cases/{caseRef}/documents/{documentType}", UploadDocumentAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .DisableAntiforgery() // bearer-token API, no cookies to forge
            .WithSummary("RCU-ONB-003: upload or replace a §13 document (multipart field 'file'; PDF, JPEG or PNG, 10 MB).");

        group.MapGet("/cases/{caseRef}/documents/{documentType}/file", DownloadDocumentAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("RCU-ONB-003: the stored file of a §13 document.");

        group.MapPost("/cases/{caseRef}/documents/{documentType}/review", ReviewDocumentAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("RCU-ONB-003: verify or reject an uploaded document (a rejection needs a note).");

        group.MapGet("/cases/{caseRef}/file-status", FileStatusAsync)
            .RequireAuthorization(OnboardingPolicies.Read)
            .WithSummary("RCU-ONB-003: { complete, missing[] } — mandatory documents not verified yet.");

        group.MapPost("/cases/{caseRef}/file-complete", CompleteFileAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("RCU-ONB-003: mark the file complete, or 409 file_incomplete listing the missing documents.");

        group.MapPost("/cases/{caseRef}/milestones/{milestoneId:guid}/complete", CompleteMilestoneAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("RCU-ONB-004: record a touchpoint or probation milestone as done, with notes.");

        group.MapPost("/cases/{caseRef}/probation/decision", DecideAsync)
            .RequireAuthorization(OnboardingPolicies.Decide)
            .WithSummary("RCU-ONB-005: confirm (needs a complete file and cleared BGV) or extend (reason, 1–6 months) at the end of probation.");

        group.MapGet("/cases/{caseRef}/confirmation-letter", ConfirmationLetterAsync)
            .RequireAuthorization(OnboardingPolicies.Operate)
            .WithSummary("RCU-ONB-005: the confirmation letter of a confirmed employee (PDF).");

        return app;
    }

    private static async Task<IResult> ListAsync(string? status, IQueryHandler<ListCasesQuery, IReadOnlyList<OnboardingCaseDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListCasesQuery(status), ct)).ToHttpResult();

    private static async Task<IResult> GetAsync(string caseRef, IQueryHandler<GetCaseQuery, OnboardingCaseDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetCaseQuery(caseRef), ct)).ToHttpResult();

    private static async Task<IResult> SetChecklistItemAsync(
        string caseRef,
        string itemKey,
        ChecklistItemRequest request,
        ICommandHandler<SetChecklistItemCommand, OnboardingCaseDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SetChecklistItemCommand(caseRef, itemKey, request.Done, request.Remarks), ct)).ToHttpResult();

    private static async Task<IResult> AssignAsync(string caseRef, AssignmentsRequest request, ICommandHandler<AssignPeopleCommand, OnboardingCaseDto> handler, CancellationToken ct) =>
        (await handler.Handle(new AssignPeopleCommand(caseRef, request.ReportingManagerId, request.ReportingManager, request.Buddy), ct)).ToHttpResult();

    private static async Task<IResult> UploadDocumentAsync(
        string caseRef,
        string documentType,
        IFormFile? file,
        ICommandHandler<UploadDocumentCommand, OnboardingCaseDto> handler,
        CancellationToken ct)
    {
        if (file is null)
        {
            return Error.Validation([new FieldError("file", "required", "Attach the document as the multipart field 'file'.")]).ToProblem();
        }

        await using var content = file.OpenReadStream();
        var command = new UploadDocumentCommand(caseRef, documentType, file.FileName, file.ContentType, file.Length, content);
        return (await handler.Handle(command, ct)).ToHttpResult();
    }

    private static async Task<IResult> DownloadDocumentAsync(string caseRef, string documentType, IQueryHandler<GetDocumentFileQuery, FileContent> handler, CancellationToken ct) =>
        ToFile(await handler.Handle(new GetDocumentFileQuery(caseRef, documentType), ct));

    private static async Task<IResult> ReviewDocumentAsync(
        string caseRef,
        string documentType,
        DocumentReviewRequest request,
        ICommandHandler<ReviewDocumentCommand, OnboardingCaseDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ReviewDocumentCommand(caseRef, documentType, request.Verified, request.Note), ct)).ToHttpResult();

    private static async Task<IResult> FileStatusAsync(string caseRef, IQueryHandler<GetFileStatusQuery, FileStatusDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetFileStatusQuery(caseRef), ct)).ToHttpResult();

    private static async Task<IResult> CompleteFileAsync(string caseRef, ICommandHandler<CompleteFileCommand, FileStatusDto> handler, CancellationToken ct) =>
        (await handler.Handle(new CompleteFileCommand(caseRef), ct)).ToHttpResult();

    private static async Task<IResult> CompleteMilestoneAsync(
        string caseRef,
        Guid milestoneId,
        MilestoneCompletionRequest? request,
        ICommandHandler<CompleteMilestoneCommand, OnboardingCaseDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new CompleteMilestoneCommand(caseRef, milestoneId, request?.Notes), ct)).ToHttpResult();

    private static async Task<IResult> DecideAsync(string caseRef, ProbationDecisionRequest request, ICommandHandler<DecideProbationCommand, OnboardingCaseDto> handler, CancellationToken ct) =>
        (await handler.Handle(new DecideProbationCommand(caseRef, request.Decision ?? string.Empty, request.Reason, request.ExtendByMonths), ct)).ToHttpResult();

    private static async Task<IResult> ConfirmationLetterAsync(string caseRef, IQueryHandler<GetConfirmationLetterQuery, FileContent> handler, CancellationToken ct) =>
        ToFile(await handler.Handle(new GetConfirmationLetterQuery(caseRef), ct));

    private static IResult ToFile(Result<FileContent> result) =>
        result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error!.ToProblem();
}

/// <summary>Body of <c>PUT /api/v1/onboarding/cases/{caseRef}/checklist/{itemKey}</c>.</summary>
internal sealed record ChecklistItemRequest(bool Done, string? Remarks);

/// <summary>Body of <c>PUT /api/v1/onboarding/cases/{caseRef}/assignments</c>.</summary>
internal sealed record AssignmentsRequest(string? ReportingManagerId, string? ReportingManager, string? Buddy);

/// <summary>Body of <c>POST /api/v1/onboarding/cases/{caseRef}/documents/{documentType}/review</c>.</summary>
internal sealed record DocumentReviewRequest(bool Verified, string? Note);

/// <summary>Body of <c>POST /api/v1/onboarding/cases/{caseRef}/milestones/{milestoneId}/complete</c>.</summary>
internal sealed record MilestoneCompletionRequest(string? Notes);

/// <summary>Body of <c>POST /api/v1/onboarding/cases/{caseRef}/probation/decision</c>: <c>{ decision: confirm|extend, reason, extendByMonths }</c>.</summary>
internal sealed record ProbationDecisionRequest(string? Decision, string? Reason, int? ExtendByMonths);
