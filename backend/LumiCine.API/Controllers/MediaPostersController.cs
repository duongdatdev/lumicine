using LumiCine.Application.Media;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/media/posters")]
public sealed class MediaPostersController(IPosterService posters) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var poster = await posters.OpenAsync(id, cancellationToken);
        if (poster is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(poster.Content, poster.ContentType);
    }
}
