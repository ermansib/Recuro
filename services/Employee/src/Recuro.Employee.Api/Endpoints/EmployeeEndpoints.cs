using System.Security.Claims;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Employee.Application.Ijp;
using Recuro.Employee.Application.Referrals;

namespace Recuro.Employee.Api.Endpoints;

/// <summary>
/// Employee portal API (S-16, RCU-EMP-001..005). Everything an employee reads is scoped to the token's
/// subject on the server; the email used for an IJP application is the token's when it carries one.
/// </summary>
internal static class EmployeeEndpoints
{
    private const string EmailClaim = "email";

    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/v1/employee").WithTags("Employee portal");

        portal.MapGet("/ijp", ListIjpAsync)
            .RequireAuthorization(EmployeePolicies.Browse)
            .WithSummary("RCU-EMP-001: internal openings whose IJP window is open now, closing soonest first.");

        portal.MapPut("/ijp/{reqId}", UpsertIjpAsync)
            .RequireAuthorization(EmployeePolicies.ManageIjp)
            .WithSummary("RCU-EMP-001: post or edit the internal opening for a requisition open for sourcing. The window is the configured working days from unlock.");

        portal.MapPost("/ijp/{reqId}/applications", ApplyAsync)
            .RequireAuthorization(EmployeePolicies.Self)
            .WithSummary("RCU-EMP-002: apply internally (grade band and tenure checked, one active application per requisition). Becomes an IJP-sourced Pipeline application.");

        portal.MapPost("/referrals", ReferAsync)
            .RequireAuthorization(EmployeePolicies.Self)
            .WithSummary("RCU-EMP-003: refer someone with the mandatory COI declaration (400 without it). Becomes a Referral-sourced Pipeline application.");

        portal.MapGet("/me/applications", MyApplicationsAsync)
            .RequireAuthorization(EmployeePolicies.Self)
            .WithSummary("RCU-EMP-005: the caller's own IJP applications with coarse progress.");

        portal.MapGet("/me/referrals", MyReferralsAsync)
            .RequireAuthorization(EmployeePolicies.Self)
            .WithSummary("RCU-EMP-005: the caller's own referrals with coarse progress and bonus eligibility.");

        return app;
    }

    private static async Task<IResult> ListIjpAsync(IQueryHandler<ListIjpPostingsQuery, IReadOnlyList<IjpPostingDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListIjpPostingsQuery(), ct)).ToHttpResult();

    private static async Task<IResult> UpsertIjpAsync(
        string reqId,
        IjpPostingRequest request,
        ICommandHandler<UpsertIjpPostingCommand, IjpPostingDto> handler,
        CancellationToken ct)
    {
        var command = new UpsertIjpPostingCommand(
            reqId,
            request.Title ?? string.Empty,
            request.Location ?? string.Empty,
            request.Department ?? string.Empty,
            request.Grade ?? string.Empty,
            request.EligibleGrades ?? [],
            request.MinTenureMonths ?? 0,
            request.Summary);
        return (await handler.Handle(command, ct)).ToHttpResult();
    }

    private static async Task<IResult> ApplyAsync(
        string reqId,
        IjpApplicationRequest request,
        ClaimsPrincipal user,
        ICommandHandler<ApplyInternallyCommand, MyApplicationDto> handler,
        CancellationToken ct)
    {
        var command = new ApplyInternallyCommand(
            reqId,
            user.FindFirstValue(EmailClaim) ?? request.Email,
            request.Phone,
            request.CurrentGrade ?? string.Empty,
            request.JoinedOn ?? default,
            request.ExperienceYears,
            request.PrivacyConsent);
        return (await handler.Handle(command, ct)).ToCreatedResult(_ => "/api/v1/employee/me/applications");
    }

    private static async Task<IResult> ReferAsync(
        ReferralRequest request,
        ClaimsPrincipal user,
        ICommandHandler<SubmitReferralCommand, MyReferralDto> handler,
        CancellationToken ct)
    {
        var command = new SubmitReferralCommand(
            request.ReqId ?? string.Empty,
            request.Name ?? string.Empty,
            request.Email ?? string.Empty,
            request.Phone,
            request.ExperienceYears,
            request.Relationship ?? string.Empty,
            request.CoiAccepted,
            user.FindFirstValue(EmailClaim));
        return (await handler.Handle(command, ct)).ToCreatedResult(_ => "/api/v1/employee/me/referrals");
    }

    private static async Task<IResult> MyApplicationsAsync(IQueryHandler<MyApplicationsQuery, IReadOnlyList<MyApplicationDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new MyApplicationsQuery(), ct)).ToHttpResult();

    private static async Task<IResult> MyReferralsAsync(IQueryHandler<MyReferralsQuery, IReadOnlyList<MyReferralDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new MyReferralsQuery(), ct)).ToHttpResult();
}

/// <summary>Body of <c>PUT /api/v1/employee/ijp/{reqId}</c>.</summary>
internal sealed record IjpPostingRequest(
    string? Title,
    string? Location,
    string? Department,
    string? Grade,
    IReadOnlyList<string>? EligibleGrades,
    int? MinTenureMonths,
    string? Summary);

/// <summary>Body of <c>POST /api/v1/employee/ijp/{reqId}/applications</c>. Email is only read when the token has none.</summary>
internal sealed record IjpApplicationRequest(
    string? Email,
    string? Phone,
    string? CurrentGrade,
    DateOnly? JoinedOn,
    decimal? ExperienceYears,
    bool PrivacyConsent);

/// <summary>Body of <c>POST /api/v1/employee/referrals</c>.</summary>
internal sealed record ReferralRequest(
    string? ReqId,
    string? Name,
    string? Email,
    string? Phone,
    decimal? ExperienceYears,
    string? Relationship,
    bool CoiAccepted);
