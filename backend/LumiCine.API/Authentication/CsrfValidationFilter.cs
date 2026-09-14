using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LumiCine.API.Authentication;

public sealed class CsrfValidationFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.IsHttps)
        {
            context.Result = CsrfProblemDetails.Create(context.HttpContext, "HTTPS required");
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = CsrfProblemDetails.Create(context.HttpContext, "Invalid CSRF token");
        }
    }
}

internal static class CsrfProblemDetails
{
    public static BadRequestObjectResult Create(HttpContext httpContext, string title)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
            Type = "about:blank"
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
