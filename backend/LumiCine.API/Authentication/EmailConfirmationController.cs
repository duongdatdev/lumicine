using System.ComponentModel.DataAnnotations;
using LumiCine.API.Authentication;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class EmailConfirmationController(
    IEmailConfirmationService emailConfirmationService,
    IAccountEmailService accountEmailService,
    ILogger<EmailConfirmationController> logger) : ControllerBase
{
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await emailConfirmationService.ConfirmAsync(request.UserId, request.Code, cancellationToken);
        return result == EmailConfirmationResult.Confirmed
            ? NoContent()
            : CsrfProblemDetails.Create(HttpContext, "Invalid confirmation token");
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await accountEmailService.SendConfirmationForEmailAsync(request.Email.Trim(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Confirmation email delivery failed. ErrorType: {ErrorType}", exception.GetType().Name);
        }

        return Accepted();
    }
}

public sealed class ConfirmEmailRequest
{
    [Required]
    public Guid UserId { get; init; }

    [Required]
    [MaxLength(4096)]
    public string Code { get; init; } = string.Empty;
}

public sealed class ResendConfirmationRequest : IValidatableObject
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
