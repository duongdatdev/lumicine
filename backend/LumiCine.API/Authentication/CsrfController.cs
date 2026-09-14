using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Authentication;

[ApiController]
[Route("api/auth/csrf")]
public sealed class CsrfController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRequestToken()
    {
        if (!Request.IsHttps)
        {
            return CsrfProblemDetails.Create(HttpContext, "HTTPS required");
        }

        Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }
}
