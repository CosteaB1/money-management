using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Extensions;

internal static class ResultExtensions
{
    public static IResult ToProblemDetails(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Cannot convert a successful result to a problem.");
        }

        (int status, string? title) = result.Error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Bad Request"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Server Failure"),
        };

        return Results.Problem(
            statusCode: status,
            title: title,
            type: result.Error.Code,
            detail: result.Error.Message,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.Error.Code,
                ["errorType"] = result.Error.Type.ToString(),
            });
    }

    public static IResult Match<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : ((Result)result).ToProblemDetails();
}
