using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ready(
        [FromServices] LumiCineDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? Ok(new { status = "Healthy" })
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
        }
    }
}
