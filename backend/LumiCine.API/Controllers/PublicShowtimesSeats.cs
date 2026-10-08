using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class ShowtimesController
{
    [HttpGet("{id:guid}/seats")]
    public async Task<IActionResult> GetSeats(Guid id, CancellationToken cancellationToken)
    {
        var result = await showtimes.GetSeatsAsync(id, false, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
