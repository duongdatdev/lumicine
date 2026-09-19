using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users/me")]
public sealed class CurrentUserProfileController(ICurrentUserProfileService profileService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CurrentUserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var profile = await profileService.GetAsync(userId, cancellationToken);
        return profile is null ? NotFound() : Ok(ToResponse(profile));
    }

    [HttpPut]
    [ProducesResponseType(typeof(CurrentUserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(UpdateCurrentUserProfileRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await profileService.UpdateDisplayNameAsync(userId, request.DisplayName!, cancellationToken);
        return result.Status switch
        {
            CurrentUserProfileUpdateStatus.Updated => Ok(ToResponse(result.Profile!)),
            CurrentUserProfileUpdateStatus.NotFound => NotFound(),
            CurrentUserProfileUpdateStatus.InvalidInput => BadRequest(),
            CurrentUserProfileUpdateStatus.ConcurrencyConflict => CreateConflict(),
            _ => throw new InvalidOperationException("Unknown profile update status.")
        };
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId) && userId != Guid.Empty;

    private static CurrentUserProfileResponse ToResponse(CurrentUserProfile profile) =>
        new(profile.Id, profile.Email, profile.DisplayName, profile.Roles);

    private ConflictObjectResult CreateConflict()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Profile changed",
            Type = "about:blank",
            Detail = "Reload the profile and retry the update."
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        var result = new ConflictObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}

public sealed class UpdateCurrentUserProfileRequest : IValidatableObject
{
    [Required]
    public string? DisplayName { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var displayName = DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is 0 or > 100)
        {
            yield return new ValidationResult("Display name must contain 1 to 100 characters.", [nameof(DisplayName)]);
        }
    }
}

public sealed record CurrentUserProfileResponse(Guid Id, string Email, string DisplayName, IReadOnlyCollection<string> Roles);
