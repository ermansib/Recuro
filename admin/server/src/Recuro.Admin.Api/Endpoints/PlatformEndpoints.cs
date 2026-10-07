using Recuro.Admin.Api.Auth;
using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Tenants;
using Recuro.Admin.Application.Themes;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>Platform console: Recuro's own team manages tenants and the theme library.</summary>
internal static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/v1/platform")
            .RequireAuthorization(AdminPolicies.Platform)
            .WithTags("Platform console");

        platform.MapGet("/tenants", async (ListTenantsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)));

        platform.MapGet("/tenants/{id:guid}", async (Guid id, GetTenantHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, ct)).ToHttpResult());

        platform.MapPost("/tenants", async (CreateTenantRequest request, CreateTenantHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(request, ct)).ToCreatedResult(t => $"/api/v1/platform/tenants/{t.Id}"));

        platform.MapPut("/tenants/{id:guid}", async (Guid id, UpdateTenantRequest request, UpdateTenantHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, request, ct)).ToHttpResult());

        platform.MapPut("/tenants/{id:guid}/status", async (Guid id, ChangeTenantStatusRequest request, ChangeTenantStatusHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(id, request, ct)).ToHttpResult());

        platform.MapGet("/themes", async (ListThemePresetsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(publishedOnly: false, ct)));

        platform.MapPost("/themes", async (CreateThemePresetRequest request, CreateThemePresetHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(request, ct)).ToCreatedResult(t => $"/api/v1/platform/themes/{t.Key}"));

        platform.MapPut("/themes/{key}/published", async (string key, PublishRequest request, SetThemePublishedHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(key, request.IsPublished, ct)).ToHttpResult());
    }

    internal sealed record PublishRequest(bool IsPublished);
}
