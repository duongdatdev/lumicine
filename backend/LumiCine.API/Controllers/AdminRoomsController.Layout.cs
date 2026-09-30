using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminRoomsController
{
    [Authorize(Roles = "Admin")]
    [HttpPut("rooms/{id:guid}/seats")]
    public async Task<IActionResult> ReplaceSeats(Guid id, [FromBody] IReadOnlyList<SeatLayoutRow> rows, CancellationToken ct) =>
        await Service.ReplaceSeatLayoutAsync(id, rows, ct) ? NoContent() : NotFound();
}
