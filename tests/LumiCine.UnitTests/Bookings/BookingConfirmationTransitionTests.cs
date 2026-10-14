using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;

namespace LumiCine.UnitTests.Bookings;

public sealed class BookingConfirmationTransitionTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Pending_booking_inside_hold_window_confirms()
    {
        var booking = NewBooking(holdExpiresAtUtc: Now.AddMinutes(5));

        booking.Confirm(Now.AddMinutes(1));

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(Now.AddMinutes(1), booking.UpdatedAtUtc);
    }

    [Fact]
    public void Booking_without_hold_expiry_confirms_for_legacy_records()
    {
        var booking = new Booking(Guid.NewGuid(), "B001", Guid.NewGuid(), 120, Now);

        booking.Confirm(Now);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void Booking_outside_hold_window_or_wrong_status_cannot_confirm()
    {
        var expired = new Booking(Guid.NewGuid(), "B001", Guid.NewGuid(), 120, Now.AddMinutes(-10),
            holdExpiresAtUtc: Now.AddMinutes(-1), userId: Guid.NewGuid(), requestId: Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => expired.Confirm(Now));
        Assert.Equal(BookingStatus.Pending, expired.Status);

        var confirmed = NewBooking(holdExpiresAtUtc: Now.AddMinutes(5));
        confirmed.Confirm(Now);
        Assert.Throws<InvalidOperationException>(() => confirmed.Confirm(Now.AddMinutes(1)));

        var cancelled = NewBooking(holdExpiresAtUtc: Now.AddMinutes(5));
        cancelled.Cancel(Now);
        Assert.Throws<InvalidOperationException>(() => cancelled.Confirm(Now.AddMinutes(1)));

        Assert.Throws<ArgumentException>(() => NewBooking(holdExpiresAtUtc: Now.AddMinutes(5)).Confirm(DateTime.Now));
    }

    [Fact]
    public void Seat_held_by_booking_sells_and_keeps_owner()
    {
        var bookingId = Guid.NewGuid();
        var seat = NewSeat();

        seat.Hold(bookingId);
        seat.Sell(bookingId);

        Assert.Equal(ShowtimeSeatStatus.Sold, seat.Status);
        Assert.Equal(bookingId, seat.CurrentBookingId);
    }

    [Fact]
    public void Seat_cannot_sell_when_not_held_by_the_booking()
    {
        var seat = NewSeat();
        Assert.Throws<InvalidOperationException>(() => seat.Sell(Guid.NewGuid()));

        seat.Hold(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => seat.Sell(Guid.NewGuid()));

        seat.Release(seat.CurrentBookingId!.Value);
        Assert.Equal(ShowtimeSeatStatus.Available, seat.Status);
        Assert.Throws<InvalidOperationException>(() => seat.Sell(Guid.NewGuid()));
    }

    [Fact]
    public void Issued_ticket_marks_used_once()
    {
        var ticket = new Ticket(Guid.NewGuid(), Guid.NewGuid(), "T-1", Now, Now);

        ticket.MarkUsed(Now.AddMinutes(3));

        Assert.Equal(TicketStatus.Used, ticket.Status);
        Assert.Equal(Now.AddMinutes(3), ticket.UsedAtUtc);
        Assert.Throws<InvalidOperationException>(() => ticket.MarkUsed(Now.AddMinutes(4)));
        Assert.Throws<ArgumentException>(() => ticket.MarkUsed(DateTime.Now));
    }

    private static Booking NewBooking(DateTime? holdExpiresAtUtc) =>
        new(Guid.NewGuid(), "B001", Guid.NewGuid(), 120, Now, holdExpiresAtUtc: holdExpiresAtUtc, userId: Guid.NewGuid(), requestId: Guid.NewGuid());

    private static ShowtimeSeat NewSeat() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100, Now);
}
