using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminRoomsController
{
    [Authorize(Roles = "Admin")]
    [HttpPost("cinemas/{cinemaId:guid}/rooms")]
    public async Task<IActionResult> Create(Guid cinemaId, [FromBody] RoomRequest request, CancellationToken ct)
    {
        var id = await Service.CreateRoomAsync(cinemaId, request, ct);
        return CreatedAtAction(nameof(Get), new { id }, await Service.GetAdminRoomAsync(id, ct));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("rooms/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RoomRequest request, CancellationToken ct) =>
        await Service.UpdateRoomAsync(id, request, ct) ? Ok(await Service.GetAdminRoomAsync(id, ct)) : NotFound();
}
