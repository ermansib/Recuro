using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Workspaces;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>
/// Self-service workspaces for the main portal (RCU-PLT-001). Anonymous on purpose: an organisation signs up
/// before it has an account. Sign-up is rate limited per caller; the lookup returns only sign-in settings.
/// </summary>
internal static class WorkspaceEndpoints
{
    public const string SignUpRateLimit = "workspace-sign-up";

    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var workspaces = app.MapGroup("/api/v1/workspaces")
            .AllowAnonymous()
            .WithTags("Workspaces");

        workspaces.MapPost("/", async (SignUpWorkspaceRequest request, SignUpWorkspaceHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToCreatedResult(s => $"/api/v1/workspaces/{s.Workspace.Slug}"))
            .RequireRateLimiting(SignUpRateLimit)
            .WithSummary("Creates a workspace and its owner's account from the public sign-up page.");

        workspaces.MapGet("/{slug}", async (string slug, GetWorkspaceHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(slug, ct)).ToHttpResult())
            .WithSummary("Sign-in settings of an active workspace, for its sign-in page.");
    }
}
