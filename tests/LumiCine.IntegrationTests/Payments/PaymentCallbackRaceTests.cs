using LumiCine.Application.Bookings;
using LumiCine.Application.Payments;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class PaymentCallbackRaceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Success_callback_racing_cancellation_commits_exactly_one_outcome()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats, userId) = await InsertPendingPaymentAsync(holdDuration: TimeSpan.FromMinutes(5));
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcomes = await Task.WhenAll(
            Task.Run(async () =>
            {
                try
                {
                    using var scope = factory.Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IBookingService>()
                        .CancelAsync(userId, booking.Id, CancellationToken.None);
                    return "cancelled";
                }
                catch (LumiCine.Application.Common.ApiException)
                {
                    return "rejected";
                }
            }),
            Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<IPaymentService>()
                    .ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
                return outcome.RspCode;
            }));

        await using var db = fixture.CreateDbContext();
        var storedBooking = await db.Bookings.SingleAsync(x => x.Id == booking.Id);
        var storedPayment = await db.Payments.SingleAsync(x => x.Id == payment.Id);
        var seatStates = await db.ShowtimeSeats.Where(x => seats.Contains(x.Id)).Select(x => x.Status).ToListAsync();

        if (storedBooking.Status == BookingStatus.Confirmed)
        {
            // The callback won: the booking is confirmed, seats sold, and the cancellation rejected.
            Assert.Equal(PaymentProcessingStatus.Fulfilled, storedPayment.ProcessingStatus);
            Assert.All(seatStates, status => Assert.Equal(ShowtimeSeatStatus.Sold, status));
            Assert.Equal("rejected", outcomes[0]);
            Assert.Equal(VnPayResponseCodes.Success, outcomes[1]);
            var itemIds = await db.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
            Assert.Equal(itemIds.Count, await db.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
        }
        else
        {
            // The cancellation won: the money arrived too late and is flagged for refund review.
            Assert.Equal(BookingStatus.Cancelled, storedBooking.Status);
            Assert.Equal(PaymentProcessingStatus.RequiresRefundReview, storedPayment.ProcessingStatus);
            Assert.All(seatStates, status => Assert.Equal(ShowtimeSeatStatus.Available, status));
            Assert.Equal(VnPayResponseCodes.Success, outcomes[1]);
            var lateItemIds = await db.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
            Assert.Equal(0, await db.Tickets.CountAsync(x => lateItemIds.Contains(x.BookingItemId)));
        }
    }

    [Fact]
    public async Task Success_callback_racing_expiration_never_confirms_an_expired_booking()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking, seats, _) = await InsertPendingPaymentAsync(holdDuration: TimeSpan.FromMilliseconds(50));
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcomes = await Task.WhenAll(
            Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                _ = await scope.ServiceProvider.GetRequiredService<IBookingService>()
                    .ExpireDueAsync(100, CancellationToken.None);
                return "expired";
            }),
            Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<IPaymentService>()
                    .ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
                return outcome.RspCode;
            }));

        await using var db = fixture.CreateDbContext();
        var storedBooking = await db.Bookings.SingleAsync(x => x.Id == booking.Id);
        var storedPayment = await db.Payments.SingleAsync(x => x.Id == payment.Id);
        var seatStates = await db.ShowtimeSeats.Where(x => seats.Contains(x.Id)).Select(x => x.Status).ToListAsync();
        _ = outcomes[0];

        Assert.Equal(PaymentStatus.Succeeded, storedPayment.Status);
        Assert.NotEqual(BookingStatus.Cancelled, storedBooking.Status);
        Assert.Equal(PaymentStatus.Succeeded, storedPayment.Status);
        Assert.Equal(VnPayResponseCodes.Success, outcomes[1]);
        if (storedBooking.Status == BookingStatus.Confirmed)
        {
            Assert.Equal(PaymentProcessingStatus.Fulfilled, storedPayment.ProcessingStatus);
            Assert.All(seatStates, status => Assert.Equal(ShowtimeSeatStatus.Sold, status));
        }
        else
        {
            Assert.Equal(BookingStatus.Expired, storedBooking.Status);
            Assert.Equal(PaymentProcessingStatus.RequiresRefundReview, storedPayment.ProcessingStatus);
            Assert.All(seatStates, status => Assert.Equal(ShowtimeSeatStatus.Available, status));
        }
        Assert.Equal(storedBooking.Status == BookingStatus.Confirmed ? storedPayment.ProcessingStatus : PaymentProcessingStatus.RequiresRefundReview,
            storedPayment.ProcessingStatus);
    }

    [Fact]
    public async Task Reconcile_and_ipn_running_together_issue_one_ticket_set()
    {
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IVnPayTransactionQueryClient>(
                new AdminPaymentReconcileEndpointTests.FakeQueryClient(
                    new VnPayTransactionQueryResult("00", "Confirm Success", Guid.NewGuid().ToString("N")[..16], "00", 20000))))));
        var (payment, booking, seats, _) = await InsertPendingPaymentAsync(holdDuration: TimeSpan.FromMinutes(5));
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);

        var outcomes = await Task.WhenAll(
            Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<IPaymentService>()
                    .ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
                return outcome.RspCode;
            }),
            Task.Run(async () =>
            {
                using var scope = factory.Services.CreateScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<IPaymentService>()
                    .ReconcileAsync(payment.Id, CancellationToken.None);
                return outcome.RspCode;
            }));

        Assert.Single(outcomes, code => code == VnPayResponseCodes.Success);
        Assert.Single(outcomes, code => code == VnPayResponseCodes.AlreadyProcessed);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        var itemIds = await db.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
        Assert.Equal(itemIds.Count, await db.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
        Assert.Equal(seats.Length, await db.ShowtimeSeats.CountAsync(x => seats.Contains(x.Id) && x.Status == ShowtimeSeatStatus.Sold));
    }

    private async Task<(Payment Payment, Booking Booking, Guid[] Seats, Guid UserId)> InsertPendingPaymentAsync(
        TimeSpan holdDuration)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new LumiCine.Infrastructure.Identity.ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Race test"
        });
        var booking = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 200, now.AddMinutes(-1),
            holdExpiresAtUtc: now.Add(holdDuration), userId: userId, requestId: Guid.NewGuid());
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
            $"LC{booking.Id.ToString("N")[..32]}.{Guid.NewGuid().ToString("N")[..12]}", 200, "VND",
            booking.HoldExpiresAtUtc!.Value, now);
        payment.SetPaymentUrl($"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef={payment.MerchantReference}", now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (payment, booking, seats, userId);
    }
}
