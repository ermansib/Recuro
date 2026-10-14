using Recuro.Admin.Api.Auth;
using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Roles;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>
/// Workspace roles and who holds them. The same routes exist twice: under /workspace for a workspace admin (workspace from
/// the token) and under /platform/workspaces/{id} for a platform admin acting on any workspace.
/// </summary>
internal static class RoleEndpoints
{
    public static void MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var tenant = app.MapGroup("/api/v1/workspace")
            .RequireAuthorization(AdminPolicies.Tenant)
            .WithTags("Workspace admin");
        tenant.MapGet("/roles/catalog", async (GetRoleCatalogHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)));
        MapRoutes(tenant, _ => null);

        var platform = app.MapGroup("/api/v1/platform")
            .RequireAuthorization(AdminPolicies.Platform)
            .WithTags("Platform console");
        platform.MapGet("/role-catalog", async (GetRoleCatalogHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)));
        MapRoutes(platform.MapGroup("/workspaces/{tenantId:guid}"), http => Guid.Parse((string)http.Request.RouteValues["tenantId"]!));
    }

    private static void MapRoutes(RouteGroupBuilder group, Func<HttpContext, Guid?> tenantOf)
    {
        group.MapGet("/roles", async (HttpContext http, ListRolesHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantOf(http), ct)).ToHttpResult());

        group.MapPost("/roles", async (HttpContext http, CreateRoleRequest request, CreateRoleHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantOf(http), request, ct)).ToCreatedResult(r => $"{http.Request.Path}/{r.Key}"));

        group.MapPut("/roles/{key}", async (HttpContext http, string key, UpdateRoleRequest request, UpdateRoleHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantOf(http), key, request, ct)).ToHttpResult());

        group.MapPut("/roles/{key}/status", async (HttpContext http, string key, SetRoleStatusRequest request, SetRoleStatusHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantOf(http), key, request, ct)).ToHttpResult());

        group.MapGet("/members", async (HttpContext http, ListMembersHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantOf(http), ct)).ToHttpResult());

        group.MapPut("/members/{accountId}", async (HttpContext http, string accountId, UpdateMemberRequest request, UpdateMemberHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantOf(http), accountId, request, ct)).ToHttpResult());
    }
}
