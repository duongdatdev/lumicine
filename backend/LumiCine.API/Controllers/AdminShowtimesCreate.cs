using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminShowtimesController
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShowtimeRequest request, CancellationToken cancellationToken)
    {
        var result = await showtimes.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/showtimes/{result.Id}", result);
    }
}
