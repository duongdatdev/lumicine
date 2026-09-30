using LumiCine.Application.Cinemas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminCinemasController
{
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CinemaRequest request, CancellationToken ct)
    {
        var id = await Service.CreateCinemaAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id }, await Service.GetAdminCinemaAsync(id, ct));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CinemaRequest request, CancellationToken ct) =>
        await Service.UpdateCinemaAsync(id, request, ct) ? Ok(await Service.GetAdminCinemaAsync(id, ct)) : NotFound();

}
