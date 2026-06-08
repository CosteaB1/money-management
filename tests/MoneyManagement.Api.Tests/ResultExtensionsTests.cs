using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using MoneyManagement.Api.Extensions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Unit coverage for <see cref="ResultExtensions"/> — the error-type to
/// ProblemDetails status mapping and the success-guard. No HTTP pipeline needed.
/// </summary>
public sealed class ResultExtensionsTests
{
    [Fact]
    public void ToProblemDetails_OnSuccessResult_Throws()
    {
        var success = Result.Success();

        Action act = () => success.ToProblemDetails();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot convert a successful result to a problem.");
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void ToProblemDetails_MapsErrorTypeToStatus(ErrorType type, int expectedStatus)
    {
        var error = new Error("some.code", "Something went wrong.", type);
        var failure = Result.Failure(error);

        IResult problem = failure.ToProblemDetails();

        problem.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public void Match_OnSuccess_InvokesOnSuccessCallback()
    {
        var result = Result.Success(42);

        IResult mapped = result.Match(value => Results.Ok(value));

        mapped.Should().BeOfType<Ok<int>>().Which.Value.Should().Be(42);
    }

    [Fact]
    public void Match_OnFailure_ReturnsProblemDetails()
    {
        var result = Result.Failure<int>(
            new Error("x.not_found", "missing", ErrorType.NotFound));

        IResult mapped = result.Match(Results.Ok);

        mapped.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}
