using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Candidate.Application.Candidates;
using Recuro.Candidate.Application.Candidates.Commands.CreateCandidate;
using Recuro.Candidate.Application.Candidates.Commands.PurgeCandidates;
using Recuro.Candidate.Application.Candidates.Commands.SetLegalHold;
using Recuro.Candidate.Application.Candidates.Commands.UploadResume;
using Recuro.Candidate.Application.Candidates.Queries.GetCandidate;
using Recuro.Candidate.Application.Candidates.Queries.GetCandidates;
using Recuro.Candidate.Application.Candidates.Queries.GetResume;

namespace Recuro.Candidate.Api.Endpoints;

/// <summary>Candidate master API (RCU-CND-001..005). Responses use the frontend's <c>Candidate</c> shape.</summary>
internal static class CandidateEndpoints
{
    /// <summary>Consents captured by HR-TA when logging a candidate on someone's behalf.</summary>
    private const string HrLoggedConsentSource = "hr-logged";

    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/candidates").WithTags("Candidates");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(CandidatePolicies.Create)
            .WithSummary("RCU-CND-001/005: create a candidate with consents and source attribution. 409 duplicate_candidate names the existing record.");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(CandidatePolicies.Read)
            .WithSummary("RCU-CND-004: one candidate, masked for the caller's role (frontend getCandidate).");

        group.MapGet("/", GetManyAsync)
            .RequireAuthorization(CandidatePolicies.Read)
            .WithSummary("Several candidates by id (?ids=a,b,c, at most 200), masked for the caller's role. Used by the pipeline board.");

        group.MapPut("/{id:guid}/legal-hold", SetLegalHoldAsync)
            .RequireAuthorization(CandidatePolicies.Govern)
            .WithSummary("RCU-CND-003: pin a candidate against retention purge, or release the pin.");

        group.MapPost("/retention/purge", PurgeAsync)
            .RequireAuthorization(CandidatePolicies.Govern)
            .WithSummary("RCU-CND-003: anonymise this tenant's candidates past retention now. dryRun reports the impact only.");

        group.MapPut("/{id:guid}/resume", UploadResumeAsync)
            .RequireAuthorization(CandidatePolicies.Create)
            .DisableAntiforgery() // bearer-token API, no cookies to forge
            .WithSummary("Upload or replace the candidate's CV (multipart field 'file'; PDF or Word, 5 MB).");

        group.MapGet("/{id:guid}/resume", DownloadResumeAsync)
            .RequireAuthorization(CandidatePolicies.ReadResume)
            .WithSummary("Download the candidate's CV.");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateCandidateRequest request,
        ICommandHandler<CreateCandidateCommand, CandidateDto> handler,
        CancellationToken ct)
    {
        var command = new CreateCandidateCommand(
            request.Name ?? string.Empty,
            request.Email ?? string.Empty,
            request.Phone,
            request.ExperienceYears,
            request.Summary,
            request.CurrentCtc,
            request.ExpectedCtc,
            request.NoticeDays,
            request.Source ?? string.Empty,
            request.SourceRef,
            request.ReferrerId,
            request.ConsultantId,
            request.Channel,
            request.Consents ?? [],
            request.ConsentSource ?? HrLoggedConsentSource);
        return (await handler.Handle(command, ct)).ToCreatedResult(dto => $"/api/v1/candidates/{dto.Id}");
    }

    private static async Task<IResult> GetAsync(Guid id, IQueryHandler<GetCandidateQuery, CandidateDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetCandidateQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> GetManyAsync(
        string? ids,
        IQueryHandler<GetCandidatesQuery, IReadOnlyList<CandidateDto>> handler,
        CancellationToken ct)
    {
        var parsed = new List<Guid>();
        foreach (var part in (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                return Error.Validation([new FieldError("ids", "invalid_id", $"'{part}' is not a candidate id.")]).ToProblem();
            }

            parsed.Add(id);
        }

        return (await handler.Handle(new GetCandidatesQuery(parsed), ct)).ToHttpResult();
    }

    private static async Task<IResult> SetLegalHoldAsync(
        Guid id,
        LegalHoldRequest request,
        ICommandHandler<SetLegalHoldCommand> handler,
        CancellationToken ct) =>
        (await handler.Handle(new SetLegalHoldCommand(id, request.OnHold, request.Reason), ct)).ToHttpResult();

    private static async Task<IResult> PurgeAsync(
        PurgeRequest request,
        ICommandHandler<PurgeCandidatesCommand, PurgeReportDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new PurgeCandidatesCommand(request.DryRun, request.Limit), ct)).ToHttpResult();

    private static async Task<IResult> UploadResumeAsync(
        Guid id,
        IFormFile? file,
        ICommandHandler<UploadResumeCommand, ResumeDto> handler,
        CancellationToken ct)
    {
        if (file is null)
        {
            return Error.Validation([new FieldError("file", "required", "Attach the CV as the multipart field 'file'.")]).ToProblem();
        }

        await using var content = file.OpenReadStream();
        var command = new UploadResumeCommand(id, file.FileName, file.ContentType, file.Length, content);
        return (await handler.Handle(command, ct)).ToHttpResult();
    }

    private static async Task<IResult> DownloadResumeAsync(Guid id, IQueryHandler<GetResumeQuery, ResumeContent> handler, CancellationToken ct)
    {
        var result = await handler.Handle(new GetResumeQuery(id), ct);
        return result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.Metadata.ContentType, result.Value.Metadata.FileName)
            : result.Error!.ToProblem();
    }
}

/// <summary>Body of <c>POST /api/v1/candidates</c>.</summary>
internal sealed record CreateCandidateRequest(
    string? Name,
    string? Email,
    string? Phone,
    decimal ExperienceYears,
    string? Summary,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays,
    string? Source,
    string? SourceRef,
    string? ReferrerId,
    string? ConsultantId,
    string? Channel,
    IReadOnlyList<ConsentInput>? Consents,
    string? ConsentSource);

/// <summary>Body of <c>PUT /api/v1/candidates/{id}/legal-hold</c>.</summary>
internal sealed record LegalHoldRequest(bool OnHold, string? Reason);

/// <summary>Body of <c>POST /api/v1/candidates/retention/purge</c>.</summary>
internal sealed record PurgeRequest(bool DryRun, int? Limit);
