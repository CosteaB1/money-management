using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MoneyManagement.Api.Infrastructure;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problemDetails;

        if (IsMalformedRequest(exception, out int badRequestStatusCode))
        {
            logger.LogWarning(
                exception,
                "Malformed request while processing {Path}",
                httpContext.Request.Path);

            problemDetails = new ProblemDetails
            {
                Status = badRequestStatusCode,
                Title = "Bad Request",
                Detail = "The request body is malformed or contains invalid values.",
            };
        }
        else
        {
            logger.LogError(exception, "Unhandled exception while processing {Path}", httpContext.Request.Path);

            problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server Failure",
                Detail = "An unexpected error occurred.",
            };
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static bool IsMalformedRequest(Exception exception, out int statusCode)
    {
        // Minimal-API request-body binding throws BadHttpRequestException (often wrapping a
        // JsonException) for unmapped enum strings or malformed JSON. These are client input
        // errors, not server faults.
        switch (exception)
        {
            case BadHttpRequestException badRequest:
                statusCode = badRequest.StatusCode;
                return true;
            case JsonException:
                statusCode = StatusCodes.Status400BadRequest;
                return true;
            default:
                statusCode = StatusCodes.Status500InternalServerError;
                return false;
        }
    }
}
