using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;

namespace Recuro.BuildingBlocks.UnitTests;

public class ResultHttpTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    public void Errors_map_to_the_statuses_the_frontend_mock_uses(ErrorType type, int status)
    {
        var problem = Assert.IsType<ProblemHttpResult>(new Error("code", "message", type).ToProblem());

        Assert.Equal(status, problem.StatusCode);
        Assert.Equal("code", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void Validation_problems_carry_field_errors()
    {
        var error = Error.Validation([new FieldError("grade", "required", "Grade is required")]);

        var problem = Assert.IsType<ProblemHttpResult>(error.ToProblem());

        var fields = Assert.IsType<IReadOnlyList<FieldError>>(problem.ProblemDetails.Extensions["errors"], exactMatch: false);
        Assert.Equal("grade", Assert.Single(fields).Field);
        Assert.IsType<ProblemDetails>(problem.ProblemDetails, exactMatch: false);
    }
}
