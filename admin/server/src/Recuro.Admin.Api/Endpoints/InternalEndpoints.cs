using Recuro.Admin.Api.Auth;
using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Roles;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>
/// Reads for Recuro's microservices, signed in with their service accounts (RCU-AUT-005). Never for browsers:
/// people reach workspace data only through the tenant and platform routes.
/// </summary>
internal static class InternalEndpoints
{
    public static void MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var internalApi = app.MapGroup("/api/v1/internal")
            .RequireAuthorization(AdminPolicies.Service)
            .WithTags("Internal");

        internalApi.MapGet("/workspaces/{workspaceId:guid}/access", async (Guid workspaceId, GetWorkspaceAccessHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(workspaceId, ct)).ToHttpResult());
    }
}
