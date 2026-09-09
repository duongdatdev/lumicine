using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.IntegrationTests;

[ApiController]
[Route("__test/exception-probe")]
public sealed class ExceptionProbeController : ControllerBase
{
    [HttpGet("unexpected")]
    public IActionResult Unexpected() => throw new InvalidOperationException("sensitive-test-exception-message");

    [HttpGet("bad-request")]
    public IActionResult LargeRequest() => throw new BadHttpRequestException(
        "sensitive-test-bad-request-message",
        StatusCodes.Status413PayloadTooLarge);
}
