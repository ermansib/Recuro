using Recuro.Admin.Api.Auth;
using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Branding;
using Recuro.Admin.Application.Screens;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>Tenant admin: each customer configures its own portal. The tenant always comes from the token, never the URL.</summary>
internal static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var tenant = app.MapGroup("/api/v1/workspace")
            .RequireAuthorization(AdminPolicies.Tenant)
            .WithTags("Workspace admin");

        // Read-only: the platform console assigns each tenant's theme (PUT /api/v1/platform/workspaces/{id}/theme).
        tenant.MapGet("/branding", async (GetBrandingHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(ct)).ToHttpResult());

        tenant.MapGet("/screens", async (ListScreensHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(ct)).ToHttpResult());

        tenant.MapGet("/screens/{key}", async (string key, GetScreenHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(key, ct)).ToHttpResult());

        tenant.MapPut("/screens/{key}", async (string key, UpdateScreenRequest request, UpdateScreenHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(key, request, ct)).ToHttpResult());
    }
}
