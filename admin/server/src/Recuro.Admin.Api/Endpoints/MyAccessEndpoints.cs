using System.Security.Claims;
using Recuro.Admin.Api.Auth;
using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Roles;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>
/// The signed-in main-portal person's own access (RCU-PLT-002). The workspace and account come from their token only, so
/// a person can never read another workspace's roles or another person's assignments.
/// </summary>
internal static class MyAccessEndpoints
{
    /// <summary>Realm roles as Keycloak maps them into the token (see admin/keycloak/recuro-realm.json).</summary>
    private const string RealmRolesClaim = "roles";

    public static void MapMyAccessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me/access", async (ClaimsPrincipal user, GetMyAccessHandler handler, CancellationToken ct) =>
            {
                if (!Guid.TryParse(user.FindFirstValue(AdminClaims.Tenant), out var workspaceId))
                {
                    return Results.Forbid();
                }

                var tokenRoles = user.FindAll(AdminClaims.WorkspaceRoles).Concat(user.FindAll(RealmRolesClaim)).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
                var requester = new AccessRequester(workspaceId, user.FindFirstValue(AdminClaims.Subject)!, tokenRoles);
                return (await handler.HandleAsync(requester, ct)).ToHttpResult();
            })
            .RequireAuthorization(AdminPolicies.WorkspacePerson)
            .WithTags("Main portal")
            .WithSummary("The signed-in person's roles, permissions and pages in their own workspace.");
    }
}
