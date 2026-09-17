using System.ComponentModel.DataAnnotations;
using LumiCine.API.Authentication;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class RegistrationController(
    IAccountRegistrationService registrationService,
    IAccountEmailService accountEmailService,
    ILogger<RegistrationController> logger) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registrationService.RegisterAsync(
            request.Email.Trim(),
            request.DisplayName.Trim(),
            request.Password,
            cancellationToken);

        if (result.Status == AccountRegistrationStatus.DuplicateEmail)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Email already registered",
                Type = "about:blank"
            };
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return ProblemResult(problem, StatusCodes.Status409Conflict);
        }

        if (result.Status == AccountRegistrationStatus.InvalidInput)
        {
            return ValidationFailure("The email, display name, or password is invalid.");
        }

        try
        {
            await accountEmailService.SendConfirmationAsync(result.UserId!.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Confirmation email delivery failed. ErrorType: {ErrorType}", exception.GetType().Name);
        }

        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(
            result.UserId!.Value,
            result.Email!,
            result.DisplayName!,
            ConfirmationRequired: true));
    }

    private IActionResult ValidationFailure(string message)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { ["account"] = [message] })
        {
            Status = StatusCodes.Status400BadRequest
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return ProblemResult(problem, StatusCodes.Status400BadRequest);
    }

    private static ObjectResult ProblemResult(ProblemDetails problem, int statusCode)
    {
        var result = new ObjectResult(problem) { StatusCode = statusCode };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}

public sealed class RegisterRequest : IValidatableObject
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var email = Email.Trim();
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            yield return new ValidationResult("A valid email address of at most 254 characters is required.", [nameof(Email)]);
        }

        var displayName = DisplayName.Trim();
        if (displayName.Length is 0 or > 100)
        {
            yield return new ValidationResult("Display name must contain 1 to 100 characters.", [nameof(DisplayName)]);
        }
    }
}

public sealed record RegisterResponse(Guid Id, string Email, string DisplayName, bool ConfirmationRequired);
