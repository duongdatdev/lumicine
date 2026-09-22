using System.ComponentModel.DataAnnotations;
using LumiCine.API.Authentication;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class ForgotPasswordController(
    IAccountEmailService accountEmailService,
    ILogger<ForgotPasswordController> logger) : ControllerBase
{
    [HttpPost("forgot-password")]
    [EnableRateLimiting("AuthPerIp")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            await accountEmailService.SendPasswordResetForEmailAsync(request.Email.Trim(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Password reset email delivery failed. ErrorType: {ErrorType}", exception.GetType().Name);
        }

        return Accepted();
    }
}

public sealed class ForgotPasswordRequest : IValidatableObject
{
    [Required]
    public string Email { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var email = Email.Trim();
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            yield return new ValidationResult("A valid email address of at most 254 characters is required.", [nameof(Email)]);
        }
    }
}
