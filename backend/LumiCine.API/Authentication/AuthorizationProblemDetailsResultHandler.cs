using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace LumiCine.API.Authentication;

public sealed class AuthorizationProblemDetailsResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Type = "about:blank"
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                context.RequestAborted);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
