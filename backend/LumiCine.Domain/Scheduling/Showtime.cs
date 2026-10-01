using LumiCine.Domain.Common;

namespace LumiCine.Domain.Scheduling;

public sealed class Showtime
{
    private Showtime() { }

    public Showtime(
        Guid id,
        Guid movieId,
        Guid roomId,
        DateTime startAtUtc,
        DateTime endAtUtc,
        decimal basePrice,
        DateTime createdAtUtc,
        ShowtimeStatus status = ShowtimeStatus.Scheduled,
        DateTime? updatedAtUtc = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        MovieId = EntityGuard.Id(movieId, nameof(movieId));
        RoomId = EntityGuard.Id(roomId, nameof(roomId));
        StartAtUtc = EntityGuard.Utc(startAtUtc, nameof(startAtUtc));
        EndAtUtc = EntityGuard.Utc(endAtUtc, nameof(endAtUtc));
        if (EndAtUtc <= StartAtUtc)
        {
            throw new ArgumentException("End time must be later than start time.", nameof(endAtUtc));
        }

        BasePrice = EntityGuard.NonNegative(basePrice, nameof(basePrice));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        Status = EntityGuard.Defined(status, nameof(status));
        UpdatedAtUtc = EntityGuard.OptionalUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public Guid Id { get; private set; }
    public Guid MovieId { get; private set; }
    public Guid RoomId { get; private set; }
    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }
    public decimal BasePrice { get; private set; }
    public ShowtimeStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void Reschedule(DateTime startAtUtc, decimal basePrice, DateTime updatedAtUtc)
    {
        if (Status != ShowtimeStatus.Scheduled || startAtUtc <= DateTime.UtcNow)
            throw new InvalidOperationException("Only upcoming scheduled showtimes can be rescheduled.");
        var duration = EndAtUtc - StartAtUtc;
        StartAtUtc = EntityGuard.Utc(startAtUtc, nameof(startAtUtc));
        EndAtUtc = StartAtUtc.Add(duration);
        BasePrice = EntityGuard.NonNegative(basePrice, nameof(basePrice));
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void Cancel(DateTime updatedAtUtc)
    {
        if (Status != ShowtimeStatus.Scheduled || StartAtUtc <= DateTime.UtcNow)
            throw new InvalidOperationException("Only upcoming scheduled showtimes can be cancelled.");
        Status = ShowtimeStatus.Cancelled;
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }
}
