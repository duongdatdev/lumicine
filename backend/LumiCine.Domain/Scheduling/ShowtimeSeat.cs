using LumiCine.Domain.Common;

namespace LumiCine.Domain.Scheduling;

public sealed class ShowtimeSeat
{
    private ShowtimeSeat() { }

    public ShowtimeSeat(
        Guid id,
        Guid showtimeId,
        Guid roomId,
        Guid seatId,
        decimal price,
        DateTime createdAtUtc,
        ShowtimeSeatStatus status = ShowtimeSeatStatus.Available)
    {
        Id = EntityGuard.Id(id, nameof(id));
        ShowtimeId = EntityGuard.Id(showtimeId, nameof(showtimeId));
        RoomId = EntityGuard.Id(roomId, nameof(roomId));
        SeatId = EntityGuard.Id(seatId, nameof(seatId));
        Price = EntityGuard.NonNegative(price, nameof(price));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        Status = EntityGuard.Defined(status, nameof(status));
    }

    public Guid Id { get; private set; }
    public Guid ShowtimeId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid SeatId { get; private set; }
    public ShowtimeSeatStatus Status { get; private set; }
    public decimal Price { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}