using System.Text.Json;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Config.Application.Resolve;
using Recuro.Config.Application.RuleSets;
using Recuro.Config.Application.RuleSets.Commands;
using Recuro.Config.Application.RuleSets.Queries;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Api.Endpoints;

/// <summary>
/// Config API: versioned rules matrices with dual approval (RCU-CFG-001) and the resolution API every
/// rule consumer calls (RCU-CFG-002/003). Matrix types in URLs: doa, tat, offer, escalation, bgv, calendar.
/// </summary>
internal static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var versions = app.MapGroup("/api/v1/config/{matrixType}/versions").WithTags("Rule set versions");

        versions.MapGet("/", ListAsync)
            .RequireAuthorization(ConfigPolicies.Read)
            .WithSummary("Every version of a matrix, newest first, including drafts and rejections.");
        versions.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(ConfigPolicies.Read)
            .WithSummary("One version, with its content and effective dates.");
        versions.MapPost("/", ProposeAsync)
            .RequireAuthorization(ConfigPolicies.Propose)
            .WithSummary("RCU-CFG-001: propose a new version. It stays a draft until a second person approves it.");
        versions.MapPut("/{id:guid}", ReviseAsync)
            .RequireAuthorization(ConfigPolicies.Propose)
            .WithSummary("The proposer revises their draft. Active and rejected versions never change (409).");
        versions.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(ConfigPolicies.Decide)
            .WithSummary("RCU-CFG-001: a second approver activates the draft and config.version.activated.v1 is emitted.");
        versions.MapPost("/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(ConfigPolicies.Decide)
            .WithSummary("Turn a draft down, with a reason.");

        app.MapGet("/api/v1/config/rules", GetRulesAsync)
            .WithTags("Rules")
            .RequireAuthorization(ConfigPolicies.Read)
            .WithSummary("The rules part of the frontend RuleConfig (getRules): doa, offerMatrix, bgvChecks.");

        var resolve = app.MapGroup("/api/v1/resolve").WithTags("Resolve").RequireAuthorization(ConfigPolicies.Read);
        resolve.MapGet("/doa", ResolveDoaAsync)
            .WithSummary("RCU-CFG-002: the DOA route for a grade, at an instant or pinned to a version (CFG-003).");
        resolve.MapGet("/working-days", ResolveWorkingDaysAsync)
            .WithSummary("Adds working days using the location's business calendar. The backend's only working-day maths.");
        resolve.MapGet("/matrices/{matrixType}", ResolveMatrixAsync)
            .WithSummary("Any matrix in force at an instant, or pinned to a version.");

        return app;
    }

    private static IResult UnknownMatrix(string matrixType) =>
        Error.NotFound("unknown_matrix_type", $"Unknown matrix type '{matrixType}'. Known: {string.Join(", ", MatrixTypes.Keys)}.").ToProblem();

    private static async Task<IResult> ListAsync(string matrixType, IQueryHandler<ListVersionsQuery, IReadOnlyList<RuleSetVersionDto>> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new ListVersionsQuery(type), ct)).ToHttpResult()
            : UnknownMatrix(matrixType);

    private static async Task<IResult> GetAsync(string matrixType, Guid id, IQueryHandler<GetVersionQuery, RuleSetVersionDto> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new GetVersionQuery(type, id), ct)).ToHttpResult()
            : UnknownMatrix(matrixType);

    private static async Task<IResult> ProposeAsync(
        string matrixType, VersionRequest request, ICommandHandler<ProposeVersionCommand, RuleSetVersionDto> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new ProposeVersionCommand(type, request.EffectiveFrom, request.Content, request.Note), ct))
                .ToCreatedResult(dto => $"/api/v1/config/{dto.MatrixType}/versions/{dto.Id}")
            : UnknownMatrix(matrixType);

    private static async Task<IResult> ReviseAsync(
        string matrixType, Guid id, VersionRequest request, ICommandHandler<ReviseVersionCommand, RuleSetVersionDto> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new ReviseVersionCommand(type, id, request.EffectiveFrom, request.Content, request.Note), ct)).ToHttpResult()
            : UnknownMatrix(matrixType);

    private static async Task<IResult> ApproveAsync(
        string matrixType, Guid id, ICommandHandler<ApproveVersionCommand, RuleSetVersionDto> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new ApproveVersionCommand(type, id), ct)).ToHttpResult()
            : UnknownMatrix(matrixType);

    private static async Task<IResult> RejectAsync(
        string matrixType, Guid id, RejectRequest request, ICommandHandler<RejectVersionCommand, RuleSetVersionDto> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new RejectVersionCommand(type, id, request.Reason ?? string.Empty), ct)).ToHttpResult()
            : UnknownMatrix(matrixType);

    private static async Task<IResult> GetRulesAsync(DateTimeOffset? at, IQueryHandler<GetRulesQuery, RulesDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetRulesQuery(at), ct)).ToHttpResult();

    private static async Task<IResult> ResolveDoaAsync(
        string? grade, string? budget, DateTimeOffset? at, Guid? versionId, IQueryHandler<ResolveDoaQuery, ResolvedDoaDto> handler, CancellationToken ct) =>
        (await handler.Handle(new ResolveDoaQuery(grade, budget, at, versionId), ct)).ToHttpResult();

    private static async Task<IResult> ResolveWorkingDaysAsync(
        DateOnly? from, int? days, string? location, Guid? versionId, IQueryHandler<ResolveWorkingDaysQuery, WorkingDaysDto> handler, CancellationToken ct) =>
        (await handler.Handle(new ResolveWorkingDaysQuery(from, days, location, versionId), ct)).ToHttpResult();

    private static async Task<IResult> ResolveMatrixAsync(
        string matrixType, DateTimeOffset? at, Guid? versionId, IQueryHandler<ResolveMatrixQuery, ResolvedMatrixDto> handler, CancellationToken ct) =>
        MatrixTypes.TryParse(matrixType, out var type)
            ? (await handler.Handle(new ResolveMatrixQuery(type, at, versionId), ct)).ToHttpResult()
            : UnknownMatrix(matrixType);
}

/// <summary>Body of a propose or revise: when it takes effect, the matrix, and an optional note for the approver.</summary>
internal sealed record VersionRequest(DateTimeOffset? EffectiveFrom, JsonElement Content, string? Note);

/// <summary>Body of a reject.</summary>
internal sealed record RejectRequest(string? Reason);
