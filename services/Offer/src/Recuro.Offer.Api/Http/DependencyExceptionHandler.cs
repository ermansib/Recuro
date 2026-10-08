using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Offer.Application.Abstractions;

namespace Recuro.Offer.Api.Http;

/// <summary>
/// Expected infrastructure failures as problem details: a dependency that didn't answer is 503 (retry),
/// a concurrent edit is 409 (reload). Everything else stays a 500.
/// </summary>
internal sealed partial class DependencyExceptionHandler(ILogger<DependencyExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        IResult? result = exception switch
        {
            DependencyUnavailableException => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "A service this request depends on is unavailable. Try again shortly.",
                extensions: new Dictionary<string, object?> { ["code"] = "dependency_unavailable" }),
            DbUpdateConcurrencyException => Error.Conflict("stale_version", "The record was changed by someone else. Reload it and try again.").ToProblem(),
            _ => null,
        };
        if (result is null)
        {
            return false;
        }

        DependencyFailed(logger, exception);
        await result.ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request failed on a dependency")]
    private static partial void DependencyFailed(ILogger logger, Exception ex);
}
