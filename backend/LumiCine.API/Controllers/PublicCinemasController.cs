using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/cinemas")]
public sealed class CinemasController(ICinemaService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? city = null, CancellationToken ct = default) =>
        Ok(await service.GetPublicCinemasAsync(new(page, pageSize), city, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await service.GetPublicCinemaAsync(id, ct) is { } result ? Ok(result) : NotFound();
}
