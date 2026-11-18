using LumiCine.Application.Scheduling;

namespace LumiCine.API.Realtime;

internal sealed class NoOpShowtimeAvailabilityNotifier : IShowtimeAvailabilityNotifier
{
    public Task NotifyAsync(Guid showtimeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
