using Recuro.Admin.Application.Tenants;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>Lists the personas the development sign-in offers. Mapped only when Auth:Mode is Development.</summary>
internal static class DevelopmentEndpoints
{
    public static void MapDevelopmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/dev/personas", async (ListTenantsHandler tenants, CancellationToken ct) =>
            {
                var list = await tenants.HandleAsync(ct);
                IEnumerable<PersonaDto> personas =
                [
                    new("platform", "Platform admin", null),
                    .. list.Select(t => new PersonaDto($"tenant:{t.Id}", "Tenant admin", t.Name)),
                ];
                return Results.Ok(personas);
            })
            .AllowAnonymous()
            .WithTags("Development");
    }

    internal sealed record PersonaDto(string Id, string Role, string? TenantName);
}
