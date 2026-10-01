using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminCinemasController
{
    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] CinemaStatusRequest request, CancellationToken ct) =>
        await Service.SetCinemaActiveAsync(id, request.IsActive!.Value, ct) ? NoContent() : NotFound();
}
