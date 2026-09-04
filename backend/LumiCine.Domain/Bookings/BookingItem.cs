using LumiCine.Domain.Common;

namespace LumiCine.Domain.Bookings;

public sealed class BookingItem
{
    private BookingItem() { }

    public BookingItem(
        Guid id,
        Guid bookingId,
        Guid showtimeId,
        Guid showtimeSeatId,
        decimal unitPrice,
        DateTime createdAtUtc)
    {
        Id = EntityGuard.Id(id, nameof(id));
        BookingId = EntityGuard.Id(bookingId, nameof(bookingId));
        ShowtimeId = EntityGuard.Id(showtimeId, nameof(showtimeId));
        ShowtimeSeatId = EntityGuard.Id(showtimeSeatId, nameof(showtimeSeatId));
        UnitPrice = EntityGuard.NonNegative(unitPrice, nameof(unitPrice));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid ShowtimeId { get; private set; }
    public Guid ShowtimeSeatId { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}