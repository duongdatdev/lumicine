using LumiCine.Application.Payments;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    [Fact]
    public async Task Fulfillment_issues_one_ticket_per_seat_and_queues_the_confirmation_email()
    {
        var notifier = new RecordingNotifier();
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = WithNotifier(baseFactory, notifier);
        var (payment, booking, seats) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
        Assert.Equal([booking.ShowtimeId], notifier.Showtimes);
        await using var db = fixture.CreateDbContext();
        var tickets = await (from ticket in db.Tickets
                             join item in db.BookingItems on ticket.BookingItemId equals item.Id
                             where item.BookingId == booking.Id
                             select ticket).ToListAsync();
        Assert.Equal(seats.Length, tickets.Count);
        Assert.All(tickets, ticket => Assert.Matches("^[A-Za-z0-9_-]{43}$", ticket.Code));
        Assert.Equal(tickets.Count, tickets.Select(x => x.Code).ToHashSet().Count);
        Assert.All(tickets, ticket => Assert.Equal(TicketStatus.Issued, ticket.Status));
        var delivery = await db.TicketEmailDeliveries.SingleAsync(x => x.BookingId == booking.Id);
        Assert.Equal(TicketEmailDeliveryStatus.Pending, delivery.Status);
    }

    [Fact]
    public async Task Notification_failure_after_payment_keeps_the_committed_result()
    {
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = WithNotifier(baseFactory, new FailingNotifier());
        var (payment, booking, _) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        Assert.Equal(1, await db.TicketEmailDeliveries.CountAsync(x => x.BookingId == booking.Id));
    }

    [Fact]
    public async Task Email_queue_failure_rolls_back_the_whole_confirmation()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats) = await InsertPendingPaymentAsync();
        await using (var db = fixture.CreateDbContext())
        {
            // A pending delivery row already exists for the booking, so the confirmation insert
            // violates its uniqueness and the entire transaction must roll back.
            db.TicketEmailDeliveries.Add(new TicketEmailDelivery(Guid.NewGuid(), booking.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var exception = await Assert.ThrowsAsync<LumiCine.Application.Common.ApiException>(() => ApplyCallbackAsync(factory, parameters));

        Assert.Equal(409, exception.StatusCode);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Pending, (await verify.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        Assert.All(await verify.ShowtimeSeats.Where(x => seats.Contains(x.Id)).ToListAsync(),
            seat => Assert.Equal(ShowtimeSeatStatus.Held, seat.Status));
        var storedPayment = await verify.Payments.SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Pending, storedPayment.Status);
        var itemIds = await verify.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
        Assert.Equal(0, await verify.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithNotifier(
        ApiWebApplicationFactory baseFactory, IShowtimeAvailabilityNotifier notifier) =>
        baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IShowtimeAvailabilityNotifier>(notifier))));

    private async Task<VnPayCallbackOutcome> ApplyCallbackAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
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

    internal sealed class RecordingNotifier : IShowtimeAvailabilityNotifier
    {
        public List<Guid> Showtimes { get; } = [];

        public Task NotifyAsync(Guid showtimeId, CancellationToken cancellationToken)
        {
            Showtimes.Add(showtimeId);
            return Task.CompletedTask;
        }
    }

    internal sealed class FailingNotifier : IShowtimeAvailabilityNotifier
    {
        public Task NotifyAsync(Guid showtimeId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected notification failure.");
    }
}
