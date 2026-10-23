using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/bookings")]
public sealed class BookingsController(
    IBookingService bookings,
    IPaymentService payments,
    ITicketService tickets) : ControllerBase
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

    [HttpPost("{id:guid}/payments/vnpay")]
    [ProducesResponseType<BookingPaymentLink>(StatusCodes.Status200OK)]
    public Task<BookingPaymentLink> CreateVnPayPayment(Guid id, CreateBookingPaymentRequest request,
        CancellationToken cancellationToken) =>
        payments.CreateBookingPaymentAsync(UserId, id, request, ClientIpAddress, cancellationToken);

    [HttpPost("{id:guid}/tickets/resend-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendTicketEmail(Guid id, CancellationToken cancellationToken)
    {
        await tickets.ResendConfirmationEmailAsync(UserId, id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/tickets")]
    [ProducesResponseType<IReadOnlyList<TicketDetails>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<TicketDetails>> ListTickets(Guid id, CancellationToken cancellationToken) =>
        tickets.ListForBookingAsync(UserId, id, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await bookings.CancelAsync(UserId, id, cancellationToken);
        return NoContent();
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    private string ClientIpAddress
    {
        get
        {
            var remote = HttpContext.Connection.RemoteIpAddress;
            if (remote is null || IPAddress.IsLoopback(remote))
                return "127.0.0.1";
            if (remote.IsIPv4MappedToIPv6)
                remote = remote.MapToIPv4();
            return remote.ToString();
        }
    }
}
