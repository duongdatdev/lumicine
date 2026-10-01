using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminRoomsController
{
    [Authorize(Roles = "Admin")]
    [HttpPut("seats/{id:guid}/status")]
    public async Task<IActionResult> SeatStatus(Guid id, [FromBody] SeatStatusRequest request, CancellationToken ct) =>
        await Service.SetSeatActiveAsync(id, request.IsActive!.Value, ct) ? NoContent() : NotFound();
}
