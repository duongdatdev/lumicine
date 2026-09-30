using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Staff")]
[Route("api/admin/cinemas")]
public sealed partial class AdminCinemasController(ICinemaService service) : ControllerBase
{
    private ICinemaService Service { get; } = service;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await Service.GetAdminCinemasAsync(new(page, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await Service.GetAdminCinemaAsync(id, ct) is { } result ? Ok(result) : NotFound();
}
