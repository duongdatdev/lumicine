using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminShowtimesController
{
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleShowtimeRequest request, CancellationToken cancellationToken)
    {
        var result = await showtimes.RescheduleAsync(id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
