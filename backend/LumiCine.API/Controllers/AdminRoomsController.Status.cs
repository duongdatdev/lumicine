using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminRoomsController
{
    [Authorize(Roles = "Admin")]
    [HttpPut("rooms/{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] RoomStatusRequest request, CancellationToken ct) =>
        await Service.SetRoomActiveAsync(id, request.IsActive!.Value, ct) ? NoContent() : NotFound();

}
