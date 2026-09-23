using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LumiCine.Application.Common;

namespace LumiCine.API.Exceptions;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;
        if (exception is ApiException)
            logger.LogInformation("Request rejected with status {StatusCode}. TraceId: {TraceId}", ((ApiException)exception).StatusCode, traceId);
        else
            logger.LogError(exception, "Unhandled request exception. TraceId: {TraceId}", traceId);

        var statusCode = exception switch
        {
            BadHttpRequestException badRequest => badRequest.StatusCode,
            ApiException apiException => apiException.StatusCode,
            _ => StatusCodes.Status500InternalServerError
        };
        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : "The request could not be processed.",
            Detail = exception is ApiException ? exception.Message : null,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = traceId;

        var problemDetailsContext = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        };
        if (await problemDetailsService.TryWriteAsync(problemDetailsContext))
        {
            return true;
        }

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
        return true;
    }
}
