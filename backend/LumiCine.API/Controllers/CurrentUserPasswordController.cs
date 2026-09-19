using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users/me/change-password")]
public sealed class CurrentUserPasswordController(IAccountPasswordService passwordService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) || userId == Guid.Empty)
        {
            return Unauthorized();
        }

        if (request.CurrentPassword is null || request.CurrentPassword.Length > 128 ||
            request.NewPassword is null || request.NewPassword.Length is < 8 or > 128)
        {
            return PasswordFailure();
        }

        var result = await passwordService.ChangePasswordAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
        return result switch
        {
            AccountPasswordChangeStatus.Changed => NoContent(),
            AccountPasswordChangeStatus.InvalidCurrentPassword or AccountPasswordChangeStatus.InvalidNewPassword => PasswordFailure(),
            AccountPasswordChangeStatus.AccountUnavailable => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown password change status.")
        };
    }

    private BadRequestObjectResult PasswordFailure()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Password change failed",
            Type = "about:blank",
            Detail = "The current password is incorrect or the new password does not meet policy."
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}

public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }
    public string? NewPassword { get; init; }
}
