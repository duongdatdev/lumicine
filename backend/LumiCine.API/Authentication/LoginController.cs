using System.ComponentModel.DataAnnotations;
using LumiCine.API.Authentication;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class LoginController(IAccountLoginService accountLoginService, IAccountRefreshService accountRefreshService) : ControllerBase
{
    private const string RefreshCookieName = "__Secure-LumiCine.Refresh";

    [HttpPost("login")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await accountLoginService.LoginAsync(request.Email.Trim(), request.Password, cancellationToken);
        if (result is null)
        {
            Response.Headers.CacheControl = "no-store";
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "about:blank"
            };
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            var unauthorized = new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized };
            unauthorized.ContentTypes.Add("application/problem+json");
            return unauthorized;
        }

        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = new DateTimeOffset(result.RefreshTokenExpiresAtUtc)
        });
        return Ok(new LoginResponse(
            result.AccessToken,
            "Bearer",
            result.ExpiresInSeconds,
            new LoginUserResponse(result.UserId, result.Email, result.DisplayName, result.Roles)));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CsrfValidationFilter))]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken);
        var result = await accountRefreshService.RefreshAsync(refreshToken ?? string.Empty, cancellationToken);
        if (result is null)
        {
            Response.Cookies.Delete(RefreshCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth"
            });
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "about:blank"
            };
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            var unauthorized = new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized };
            unauthorized.ContentTypes.Add("application/problem+json");
            return unauthorized;
        }

        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = new DateTimeOffset(result.RefreshTokenExpiresAtUtc)
        });
        return Ok(new LoginResponse(
            result.AccessToken,
            "Bearer",
            result.ExpiresInSeconds,
            new LoginUserResponse(result.UserId, result.Email, result.DisplayName, result.Roles)));
    }
}

public sealed class LoginRequest : IValidatableObject
{
    [Required]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var email = Email.Trim();
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            yield return new ValidationResult("A valid email address of at most 254 characters is required.", [nameof(Email)]);
        }
    }
}

public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn, LoginUserResponse User);
public sealed record LoginUserResponse(Guid Id, string Email, string DisplayName, IReadOnlyCollection<string> Roles);
