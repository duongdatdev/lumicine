using LumiCine.Domain.Bookings;

namespace LumiCine.UnitTests.Bookings;

public sealed class BookingEntityTests
{
    [Fact]
    public void Booking_requires_valid_code_ids_and_nonnegative_total()
    {
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), " ", Guid.NewGuid(), 10, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), "B001", Guid.Empty, 10, DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Booking(Guid.NewGuid(), "B001", Guid.NewGuid(), -1, DateTime.UtcNow));
    }

    [Fact]
    public void Booking_requires_utc_optional_hold_expiration()
    {
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), "B001", Guid.NewGuid(), 10, DateTime.UtcNow, holdExpiresAtUtc: DateTime.Now));
    }

    [Fact]
    public void Booking_item_requires_ids_and_nonnegative_price()
    {
        Assert.Throws<ArgumentException>(() => new BookingItem(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 10, DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BookingItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1, DateTime.UtcNow));
    }

    [Fact]
    public void Ticket_requires_code_item_id_and_utc_times()
    {
        Assert.Throws<ArgumentException>(() => new Ticket(Guid.NewGuid(), Guid.Empty, "T001", DateTime.UtcNow, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new Ticket(Guid.NewGuid(), Guid.NewGuid(), "T001", DateTime.UtcNow, DateTime.UtcNow, usedAtUtc: DateTime.Now));
    }

    [Fact]
    public void Booking_and_ticket_accept_zero_total_and_default_statuses()
    {
        var now = DateTime.UtcNow;
        var booking = new Booking(Guid.NewGuid(), "B001", Guid.NewGuid(), 0, now);
        var ticket = new Ticket(Guid.NewGuid(), Guid.NewGuid(), "T001", now, now);

        Assert.Equal(0, booking.TotalAmount);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(TicketStatus.Issued, ticket.Status);
    }
}