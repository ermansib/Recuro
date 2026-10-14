using System.Security.Claims;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Identity.Application.Access.Queries.Decide;
using Recuro.Identity.Application.Masking.Queries.GetMaskingMap;
using Recuro.Identity.Application.Users;
using Recuro.Identity.Application.Users.Commands.SyncCurrentUser;
using Recuro.Identity.Application.Users.Queries.GetUser;
using Recuro.Identity.Application.Users.Queries.ListUsers;

namespace Recuro.Identity.Api.Endpoints;

/// <summary>
/// Identity API (RCU-AUT-001, 003, 004). Keycloak signs people in; this service mirrors them, decides
/// access and serves the masking map. Every endpoint needs a signed-in tenant member (the fallback policy).
/// </summary>
internal static class IdentityEndpoints
{
    private const string EmailClaim = "email";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identity").WithTags("Identity");

        group.MapGet("/me", MeAsync)
            .WithSummary("RCU-AUT-001: the signed-in person, provisioned just in time from their token (frontend User).");

        group.MapGet("/users", ListUsersAsync)
            .RequireAuthorization(IdentityPolicies.ReadUsers)
            .WithSummary("The tenant's people (frontend listTeam); filter by role to find recipients, and by department for a department's head (role=hod&department=…).");

        group.MapGet("/users/{id}", GetUserAsync)
            .RequireAuthorization(IdentityPolicies.ReadUsers)
            .WithSummary("One person of the tenant by user id, e.g. a reporting manager; 404 when unknown.");

        group.MapPost("/decide", DecideAsync)
            .WithSummary("RCU-AUT-003: policy decision point. Allow/deny with reasons; default deny; cacheable for ttlSeconds.");

        group.MapGet("/masking/{role}/{resource}", MaskingAsync)
            .WithSummary("RCU-AUT-004: field → mask strategy (hide / partial / hash) for a role on a resource, e.g. /masking/mdceo/candidate.");

        return app;
    }

    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user,
        ICommandHandler<SyncCurrentUserCommand, UserDto> handler,
        CancellationToken ct)
    {
        var command = new SyncCurrentUserCommand(
            user.FindFirst(RecuroClaims.Subject)?.Value ?? string.Empty,
            user.FindFirst(RecuroClaims.Name)?.Value ?? user.FindFirst(RecuroClaims.PreferredUsername)?.Value,
            user.FindFirst(EmailClaim)?.Value,
            user.FindAll(RecuroClaims.Roles).Select(c => c.Value).ToList(),
            user.FindFirst(RecuroClaims.Department)?.Value,
            user.FindFirst(RecuroClaims.Manager)?.Value);
        return (await handler.Handle(command, ct)).ToHttpResult();
    }

    private static async Task<IResult> ListUsersAsync(
        string? role,
        string? department,
        IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListUsersQuery(role, department), ct)).ToHttpResult();

    private static async Task<IResult> GetUserAsync(
        string id,
        IQueryHandler<GetUserQuery, UserDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetUserQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> DecideAsync(
        DecideRequest request,
        IQueryHandler<DecideQuery, DecisionDto> handler,
        CancellationToken ct)
    {
        var query = new DecideQuery(
            request.Actor?.Id ?? string.Empty,
            request.Actor?.Roles ?? [],
            request.Action ?? string.Empty,
            request.Resource?.Type,
            request.Resource?.Id,
            request.Resource?.AssigneeIds ?? [],
            request.Context?.Mfa ?? false);
        return (await handler.Handle(query, ct)).ToHttpResult();
    }

    private static async Task<IResult> MaskingAsync(
        string role,
        string resource,
        IQueryHandler<GetMaskingMapQuery, MaskingMapDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetMaskingMapQuery(role, resource), ct)).ToHttpResult();
}

/// <summary>Body of <c>POST /api/v1/identity/decide</c>: <c>{ actor, action, resource, context }</c>.</summary>
internal sealed record DecideRequest(DecideActor? Actor, string? Action, DecideResource? Resource, DecideContext? Context);

/// <summary>The person the calling service acts for.</summary>
internal sealed record DecideActor(string? Id, IReadOnlyList<string>? Roles);

/// <summary>The record acted on. <c>assigneeIds</c> matters for assigned-only actions.</summary>
internal sealed record DecideResource(string? Type, string? Id, IReadOnlyList<string>? AssigneeIds);

/// <summary>Session facts: <c>mfa</c> is true when the person's token shows a second factor.</summary>
internal sealed record DecideContext(bool? Mfa);
