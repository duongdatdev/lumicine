using System.ComponentModel.DataAnnotations;
using LumiCine.API.Authentication;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class ResetPasswordController(IAccountPasswordResetService passwordResetService) : ControllerBase
{
    [HttpPost("reset-password")]
    [EnableRateLimiting("AuthPerIp")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email) ||
            string.IsNullOrEmpty(request.Code) || request.Code.Length > 4096 ||
            request.NewPassword is null || request.NewPassword.Length is < 8 or > 128)
        {
            return ResetFailure();
        }

        var reset = await passwordResetService.ResetAsync(email, request.Code, request.NewPassword, cancellationToken);
        return reset ? NoContent() : ResetFailure();
    }

    private BadRequestObjectResult ResetFailure()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Password reset failed",
            Type = "about:blank",
            Detail = "The reset code or email is invalid, or the new password does not meet policy."
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}

public sealed class ResetPasswordRequest
{
    public string? Email { get; init; }
    public string? Code { get; init; }
    public string? NewPassword { get; init; }
}
