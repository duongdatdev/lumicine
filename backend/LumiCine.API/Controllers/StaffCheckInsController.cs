using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/staff")]
public sealed class StaffCheckInsController(ICheckInService checkIns) : ControllerBase
{
    [HttpPost("check-ins")]
    [ProducesResponseType<CheckInResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn(CheckInRequest request, CancellationToken cancellationToken)
    {
        var result = await checkIns.CheckInAsync(UserId, request, cancellationToken);
        return result.Outcome switch
        {
            CheckInOutcomes.Success => Ok(result),
            CheckInOutcomes.DuplicateScan => Conflict(result),
            CheckInOutcomes.TicketNotFound => throw new ApiException(404, "The scanned code does not match any ticket."),
            _ => throw new ApiException(409, CheckInRejectionMessage(result.Outcome))
        };
    }

    private static string CheckInRejectionMessage(string outcome) => outcome switch
    {
        CheckInOutcomes.BookingNotConfirmed => "The booking behind this ticket is not confirmed.",
        CheckInOutcomes.ShowtimeCancelled => "The showtime for this ticket was cancelled.",
        CheckInOutcomes.OutsideWindow => "Check-in opens 30 minutes before the showtime and closes when the movie ends.",
        _ => "This ticket cannot be used for check-in."
    };

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
