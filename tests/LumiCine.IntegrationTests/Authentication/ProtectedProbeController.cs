using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.IntegrationTests.Authentication;

[ApiController]
[Route("_test/auth")]
public sealed class ProtectedProbeController : ControllerBase
{
    [HttpGet("authenticated")]
    [Authorize]
    public IActionResult Authenticated() => Ok(new
    {
        UserId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        SessionId = User.FindFirst(JwtRegisteredClaimNames.Sid)?.Value
    });

    [HttpGet("customer")]
    [Authorize(Roles = "Customer")]
    public IActionResult Customer() => Ok("customer");

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public IActionResult Admin() => Ok("admin");
}
