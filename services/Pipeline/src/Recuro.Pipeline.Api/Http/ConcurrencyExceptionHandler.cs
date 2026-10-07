using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;

namespace Recuro.Pipeline.Api.Http;

/// <summary>
/// RCU-PPL-002: two people moved the same card at once. The second save fails its row-version check and
/// gets 409, like any other conflict, so the board reloads instead of overwriting.
/// </summary>
internal sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        var problem = Error.Conflict("concurrent_update", "The application changed while you were working on it. Reload and try again.").ToProblem();
        await problem.ExecuteAsync(httpContext);
        return true;
    }
}
