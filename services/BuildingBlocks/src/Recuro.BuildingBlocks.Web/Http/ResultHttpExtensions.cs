using Microsoft.AspNetCore.Http;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.Web.Http;

/// <summary>
/// Maps use-case results to HTTP in one place. Failures become RFC 7807 problem details with a
/// machine-readable <c>code</c>, and for validation an <c>errors</c> list of <c>{ field, code, message }</c>.
/// Statuses follow the frontend mock: 400 validation, 403 RBAC, 404 not found, 409 illegal transition.
/// </summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
    }

    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
    }

    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess ? Results.Created(location(result.Value), result.Value) : result.Error!.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        var extensions = new Dictionary<string, object?>(error.Extensions) { ["code"] = error.Code };
        if (error.Fields.Count > 0)
        {
            extensions["errors"] = error.Fields;
        }

        return Results.Problem(statusCode: status, title: error.Message, extensions: extensions);
    }
}
