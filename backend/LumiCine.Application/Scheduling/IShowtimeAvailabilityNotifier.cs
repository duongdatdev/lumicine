namespace LumiCine.Application.Scheduling;

public interface IShowtimeAvailabilityNotifier
{
    Task NotifyAsync(Guid showtimeId, CancellationToken cancellationToken);
}
