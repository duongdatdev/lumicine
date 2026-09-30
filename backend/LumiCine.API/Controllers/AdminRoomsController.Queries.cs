using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Staff")]
[Route("api/admin")]
public sealed partial class AdminRoomsController(ICinemaService service) : ControllerBase
{
    private ICinemaService Service { get; } = service;

    [HttpGet("cinemas/{cinemaId:guid}/rooms")]
    public async Task<IActionResult> GetForCinema(Guid cinemaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        await Service.GetAdminRoomsAsync(cinemaId, new(page, pageSize), ct) is { } rooms ? Ok(rooms) : NotFound();

    [HttpGet("rooms/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await Service.GetAdminRoomAsync(id, ct) is { } room ? Ok(room) : NotFound();

    [HttpGet("rooms/{id:guid}/seats")]
    public async Task<IActionResult> Seats(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        await Service.GetAdminSeatsAsync(id, new(page, pageSize), ct) is { } seats ? Ok(seats) : NotFound();
}
