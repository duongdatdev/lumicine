using LumiCine.Application.Payments;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class VnPayConfirmationTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Success_confirms_the_booking_and_sells_every_held_seat()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
        Assert.Equal(PaymentStatus.Succeeded.ToString(), outcome.Payment!.Status);
        Assert.Equal(PaymentProcessingStatus.Fulfilled.ToString(), outcome.Payment.ProcessingStatus);
        Assert.Equal(payment.Id.ToString("N")[..16], outcome.Payment.VnPayTransactionId);
        await using var db = fixture.CreateDbContext();
        var storedBooking = await db.Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, storedBooking.Status);
        var storedSeats = await db.ShowtimeSeats.Where(x => x.ShowtimeId == booking.ShowtimeId).ToListAsync();
        Assert.All(seats, seatId => Assert.Equal(ShowtimeSeatStatus.Sold, storedSeats.Single(x => x.Id == seatId).Status));
        Assert.All(storedSeats, seat => Assert.Equal(booking.Id, seat.CurrentBookingId));
    }

    [Fact]
    public async Task Success_after_the_hold_expired_flags_the_payment_for_refund_review()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats) = await InsertPendingPaymentAsync(holdDuration: TimeSpan.FromMinutes(-1));
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
        Assert.Equal(PaymentProcessingStatus.RequiresRefundReview.ToString(), outcome.Payment!.ProcessingStatus);
        // The expiration worker may already have expired the booking and released the seats;
        // either way the booking stays unconfirmed and the money is flagged for a refund.
        await using var db = fixture.CreateDbContext();
        Assert.NotEqual(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Success_for_a_cancelled_booking_flags_the_payment_for_refund_review()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, _) = await InsertPendingPaymentAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var storedBooking = await db.Bookings.SingleAsync(x => x.Id == booking.Id);
            storedBooking.Cancel(DateTime.UtcNow);
            await db.ShowtimeSeats.Where(x => x.CurrentBookingId == booking.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ShowtimeSeatStatus.Available)
                    .SetProperty(x => x.CurrentBookingId, (Guid?)null));
            await db.SaveChangesAsync();
        }
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(PaymentProcessingStatus.RequiresRefundReview.ToString(), outcome.Payment!.ProcessingStatus);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Cancelled, (await verify.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task Success_with_released_seats_never_confirms_a_partial_booking()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats) = await InsertPendingPaymentAsync();
        await using (var db = fixture.CreateDbContext())
        {
            await db.ShowtimeSeats.Where(x => x.Id == seats[0])
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ShowtimeSeatStatus.Available)
                    .SetProperty(x => x.CurrentBookingId, (Guid?)null));
        }
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
        Assert.Equal(PaymentProcessingStatus.RequiresRefundReview.ToString(), outcome.Payment!.ProcessingStatus);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Pending, (await verify.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        Assert.Equal(ShowtimeSeatStatus.Held, (await verify.ShowtimeSeats.SingleAsync(x => x.Id == seats[1])).Status);
    }

    [Fact]
    public async Task Duplicate_success_callbacks_confirm_the_booking_once()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => ApplyCallbackAsync(factory, parameters)));

        Assert.Single(outcomes, outcome => outcome.RspCode == VnPayResponseCodes.Success);
        Assert.Single(outcomes, outcome => outcome.RspCode == VnPayResponseCodes.AlreadyProcessed);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        Assert.Equal(2, await db.ShowtimeSeats.CountAsync(x => x.ShowtimeId == booking.ShowtimeId && x.Status == ShowtimeSeatStatus.Sold));
        Assert.Equal(2, await db.ShowtimeSeats.CountAsync(x => x.ShowtimeId == booking.ShowtimeId && x.CurrentBookingId == booking.Id));
    }

    private async Task<VnPayCallbackOutcome> ApplyCallbackAsync(
        ApiWebApplicationFactory factory, IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        using var scope = factory.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        return await payments.ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
    }

    private async Task<(Payment Payment, LumiCine.Domain.Bookings.Booking Booking, Guid[] Seats)> InsertPendingPaymentAsync(
        TimeSpan? holdDuration = null)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var now = DateTime.UtcNow;
        var hold = now.Add(holdDuration ?? TimeSpan.FromMinutes(5));
        var booking = new LumiCine.Domain.Bookings.Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 200, now.AddMinutes(-1),
            holdExpiresAtUtc: hold);
        db.Bookings.Add(booking);
        var seats = new Guid[2];
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            db.BookingItems.Add(new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, now));
            showtimeSeat.Hold(booking.Id);
            seats[i] = showtimeSeat.Id;
        }
        var payment = new Payment(Guid.NewGuid(), booking.Id, Guid.NewGuid(),
            $"LC{booking.Id.ToString("N")[..32]}.{Guid.NewGuid().ToString("N")[..12]}", 200, "VND", hold, now);
        payment.SetPaymentUrl($"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef={payment.MerchantReference}", now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (payment, booking, seats);
    }
}
