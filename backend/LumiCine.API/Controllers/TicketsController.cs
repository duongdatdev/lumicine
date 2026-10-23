using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LumiCine.Application.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/tickets")]
public sealed class TicketsController(ITicketService tickets) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<TicketDetails>(StatusCodes.Status200OK)]
    public Task<TicketDetails> Get(Guid id, CancellationToken cancellationToken) =>
        tickets.GetOwnedAsync(UserId, id, cancellationToken);

    [HttpGet("{id:guid}/qr")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQr(Guid id, CancellationToken cancellationToken)
    {
        var qr = await tickets.GetQrImageAsync(UserId, id, cancellationToken);
        return File(qr.Image, "image/png");
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
