using Recuro.Bgv.Application.Cases;
using Recuro.Bgv.Application.Cases.Commands;
using Recuro.Bgv.Application.Cases.Queries;
using Recuro.Bgv.Application.Dashboard;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;

namespace Recuro.Bgv.Api.Endpoints;

/// <summary>
/// BGV API (RCU-BGV-001..007). Responses use the frontend's <c>BgvCase</c> shape. <c>{caseRef}</c> is the
/// application id (frontend <c>getBgvCase(appId)</c>) or the case id (<c>reportAdverseFinding(caseId)</c>).
/// </summary>
internal static class BgvEndpoints
{
    public static IEndpointRouteBuilder MapBgvEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/bgv").WithTags("Bgv");

        group.MapGet("/dashboard/ta", TaDashboardAsync)
            .RequireAuthorization(BgvPolicies.Read)
            .WithSummary("RCU-DSH-001: the BGV in Progress tile for the gateway's TA dashboard (/bff/dashboard/ta).");

        group.MapPost("/cases", InitiateAsync)
            .RequireAuthorization(BgvPolicies.Operate)
            .WithSummary("RCU-BGV-001/002: open a case. 400 consent_required or vendor_not_active; 409 when the application is not at BGV.");

        group.MapGet("/cases/reassignment", ReassignmentQueueAsync)
            .RequireAuthorization(BgvPolicies.Read)
            .WithSummary("RCU-VND-003: open cases whose vendor was de-empanelled.");

        group.MapPost("/cases/reassign", ReassignAsync)
            .RequireAuthorization(BgvPolicies.Reassign)
            .WithSummary("RCU-VND-003: move every open case of one vendor to another active BGV agency.");

        group.MapGet("/cases/{caseRef}", GetAsync)
            .RequireAuthorization(BgvPolicies.Read)
            .WithSummary("The case for an application (frontend getBgvCase). Sensitive notes follow the masking map.");

        group.MapGet("/cases/{caseRef}/release-gate", ReleaseGateAsync)
            .RequireAuthorization(BgvPolicies.Read)
            .WithSummary("RCU-BGV-007: { cleared, blockers } while any applicable check is not cleared.");

        group.MapPost("/cases/{caseRef}/checks/{checkType}", UpdateCheckAsync)
            .RequireAuthorization(BgvPolicies.Operate)
            .WithSummary("RCU-BGV-003: record a vendor status update on one check (§6.3). 409 on an illegal move.");

        group.MapPost("/cases/{caseRef}/adverse", ReportAdverseAsync)
            .RequireAuthorization(BgvPolicies.Operate)
            .WithSummary("RCU-BGV-004/006: flag a check; locks the release and escalates (frontend reportAdverseFinding).");

        group.MapPost("/cases/{caseRef}/release", ReleaseAsync)
            .RequireAuthorization(BgvPolicies.Release)
            .WithSummary("RCU-BGV-005: 204 when the gate is open, else 409 listing the blocking checks (frontend releaseOfferAfterBgv).");

        return app;
    }

    private static async Task<IResult> TaDashboardAsync(IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetTaDashboardQuery(), ct)).ToHttpResult();

    private static async Task<IResult> InitiateAsync(InitiateCaseRequest request, ICommandHandler<InitiateCaseCommand, BgvCaseDto> handler, CancellationToken ct)
    {
        var command = new InitiateCaseCommand(
            request.AppId ?? string.Empty,
            request.VendorId ?? string.Empty,
            request.VendorCaseRef,
            request.Consent,
            request.Grade ?? string.Empty,
            request.RoleFlags ?? [],
            request.Scope);
        return (await handler.Handle(command, ct)).ToCreatedResult(dto => $"/api/v1/bgv/cases/{dto.AppId}");
    }

    private static async Task<IResult> ReassignmentQueueAsync(IQueryHandler<ListReassignmentQueueQuery, IReadOnlyList<BgvCaseDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListReassignmentQueueQuery(), ct)).ToHttpResult();

    private static async Task<IResult> ReassignAsync(ReassignRequest request, ICommandHandler<ReassignVendorCommand, ReassignmentResultDto> handler, CancellationToken ct) =>
        (await handler.Handle(new ReassignVendorCommand(request.FromVendorId ?? string.Empty, request.ToVendorId ?? string.Empty), ct)).ToHttpResult();

    private static async Task<IResult> GetAsync(string caseRef, IQueryHandler<GetCaseQuery, BgvCaseDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetCaseQuery(caseRef), ct)).ToHttpResult();

    private static async Task<IResult> ReleaseGateAsync(string caseRef, IQueryHandler<GetReleaseGateQuery, ReleaseGateDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetReleaseGateQuery(caseRef), ct)).ToHttpResult();

    private static async Task<IResult> UpdateCheckAsync(
        string caseRef,
        string checkType,
        UpdateCheckRequest request,
        ICommandHandler<UpdateCheckCommand, BgvCaseDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new UpdateCheckCommand(caseRef, checkType, request.Status ?? string.Empty, request.Note, request.SensitiveNote), ct)).ToHttpResult();

    private static async Task<IResult> ReportAdverseAsync(
        string caseRef,
        AdverseFindingRequest request,
        ICommandHandler<ReportAdverseFindingCommand, BgvCaseDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ReportAdverseFindingCommand(caseRef, request.Check ?? string.Empty, request.Description ?? string.Empty, request.Action ?? string.Empty), ct)).ToHttpResult();

    private static async Task<IResult> ReleaseAsync(string caseRef, ICommandHandler<ReleaseOfferCommand> handler, CancellationToken ct) =>
        (await handler.Handle(new ReleaseOfferCommand(caseRef), ct)).ToHttpResult();
}

/// <summary>Body of <c>POST /api/v1/bgv/cases</c>.</summary>
internal sealed record InitiateCaseRequest(
    string? AppId,
    string? VendorId,
    string? VendorCaseRef,
    ConsentInput? Consent,
    string? Grade,
    IReadOnlyList<string>? RoleFlags,
    string? Scope);

/// <summary>Body of <c>POST /api/v1/bgv/cases/{caseRef}/checks/{checkType}</c>.</summary>
internal sealed record UpdateCheckRequest(string? Status, string? Note, string? SensitiveNote);

/// <summary>Body of <c>POST /api/v1/bgv/cases/{caseRef}/adverse</c>: the frontend's finding.</summary>
internal sealed record AdverseFindingRequest(string? Check, string? Description, string? Action);

/// <summary>Body of <c>POST /api/v1/bgv/cases/reassign</c>.</summary>
internal sealed record ReassignRequest(string? FromVendorId, string? ToVendorId);
