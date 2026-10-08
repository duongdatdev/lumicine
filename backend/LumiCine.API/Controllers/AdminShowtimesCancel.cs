using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminShowtimesController
{
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        await showtimes.CancelAsync(id, cancellationToken) ? NoContent() : NotFound();
}
