using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.SignalR;

namespace LumiCine.API.Realtime;

public sealed class SignalRShowtimeAvailabilityNotifier(
    IHubContext<ShowtimeHub> hub,
    TimeProvider clock,
    ILogger<SignalRShowtimeAvailabilityNotifier> logger) : IShowtimeAvailabilityNotifier
{
    public async Task NotifyAsync(Guid showtimeId, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(ShowtimeHub.GroupName(showtimeId)).SendAsync(
                "SeatAvailabilityChanged",
                new SeatAvailabilityChanged(showtimeId, clock.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish seat availability for showtime {ShowtimeId}.", showtimeId);
        }
    }

    private sealed record SeatAvailabilityChanged(Guid ShowtimeId, DateTimeOffset OccurredAtUtc);
}
