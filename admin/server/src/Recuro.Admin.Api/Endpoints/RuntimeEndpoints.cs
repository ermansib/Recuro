using Recuro.Admin.Api.Http;
using Recuro.Admin.Application.Runtime;

namespace Recuro.Admin.Api.Endpoints;

/// <summary>
/// Read by the main Recuro portal before sign-in, to brand the login and careers pages and lay out screens.
/// Anonymous on purpose: it exposes only presentation settings, never people or hiring data.
/// </summary>
internal static class RuntimeEndpoints
{
    public static void MapRuntimeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/runtime/{slug}", async (string slug, GetRuntimeConfigHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(slug, ct)).ToHttpResult())
            .AllowAnonymous()
            .WithTags("Runtime");
    }
}
