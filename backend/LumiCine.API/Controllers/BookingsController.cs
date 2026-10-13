using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/bookings")]
public sealed class BookingsController(IBookingService bookings) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BookingDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<BookingDetails>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var (booking, created) = await bookings.CreateAsync(UserId, request, cancellationToken);
        return created ? CreatedAtAction(nameof(Get), new { id = booking.Id }, booking) : Ok(booking);
    }

    [HttpGet]
    public Task<PagedResult<BookingDetails>> List([FromQuery] string? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        bookings.ListAsync(UserId, new BookingQuery(new PageQuery(page, pageSize), status), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<BookingDetails> Get(Guid id, CancellationToken cancellationToken) =>
        await bookings.GetAsync(UserId, id, cancellationToken) ?? throw new ApiException(404, "Booking not found.");

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await bookings.CancelAsync(UserId, id, cancellationToken);
        return NoContent();
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
