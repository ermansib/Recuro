using System.Security.Claims;
using Microsoft.Extensions.Options;
using Recuro.Admin.Api.Auth;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Tenants;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>What the client needs to sign people in, and who the signed-in person is.</summary>
internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Auth");

        auth.MapGet("/config", (IOptions<AdminAuthOptions> options) =>
            {
                var value = options.Value;
                return value.IsDevelopmentMode
                    ? Results.Ok(new AuthConfigDto("development", null, null))
                    : Results.Ok(new AuthConfigDto("oidc", value.Authority, value.ClientId));
            })
            .AllowAnonymous();

        auth.MapGet("/me", async (ClaimsPrincipal user, ITenantContext tenantContext, GetTenantHandler tenants, CancellationToken ct) =>
            {
                var name = user.Identity?.Name ?? string.Empty;
                if (user.IsInRole(AdminRoles.PlatformAdmin))
                {
                    return Results.Ok(new CurrentAdminDto(name, "platform", null, null));
                }

                if (!user.IsInRole(AdminRoles.TenantAdmin) || tenantContext.TenantId is not { } tenantId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This account has no Recuro admin role.");
                }

                var tenant = await tenants.HandleAsync(tenantId, ct);
                if (tenant.IsFailure || tenant.Value.Status == TenantStatus.Suspended)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This account's tenant is not active.");
                }

                return Results.Ok(new CurrentAdminDto(name, "tenant", tenantId, tenant.Value.Name));
            })
            .RequireAuthorization();
    }

    internal sealed record AuthConfigDto(string Mode, string? Authority, string? ClientId);

    internal sealed record CurrentAdminDto(string Name, string Level, Guid? TenantId, string? TenantName);
}
