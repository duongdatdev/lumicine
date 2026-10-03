using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminShowtimesController
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? movieId, [FromQuery] Guid? cinemaId, [FromQuery] DateOnly? date,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await showtimes.GetManagementAsync(new ShowtimeQuery(movieId, cinemaId, date, new(page, pageSize)), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await showtimes.GetManagementAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
