using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class PaymentPersistenceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Merchant_reference_is_unique()
    {
        await using var db = fixture.CreateDbContext();
        var booking = await CreateBookingAsync(db);
        var reference = $"REF-{Guid.NewGuid():N}";
        await db.Payments.AddAsync(NewPayment(booking.Id, reference));
        await db.SaveChangesAsync();

        await db.Payments.AddAsync(NewPayment(booking.Id, reference, requestId: Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Request_id_is_unique_per_booking_but_reusable_across_bookings()
    {
        await using var db = fixture.CreateDbContext();
        var booking = await CreateBookingAsync(db);
        var requestId = Guid.NewGuid();
        await db.Payments.AddAsync(NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}", requestId));
        await db.SaveChangesAsync();

        var duplicate = NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}", requestId);
        await db.Payments.AddAsync(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(duplicate).State = EntityState.Detached;

        var otherBooking = await CreateBookingAsync(db);
        await db.Payments.AddAsync(NewPayment(otherBooking.Id, $"REF-{Guid.NewGuid():N}", requestId));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Booking_holds_a_single_pending_payment()
    {
        await using var db = fixture.CreateDbContext();
        var booking = await CreateBookingAsync(db);
        await db.Payments.AddAsync(NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}"));
        await db.SaveChangesAsync();

        await db.Payments.AddAsync(NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}", requestId: Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Booking_holds_a_single_fulfilled_payment()
    {
        await using var db = fixture.CreateDbContext();
        var booking = await CreateBookingAsync(db);
        var now = DateTime.UtcNow;
        var fulfilled = NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}");
        db.Payments.Add(fulfilled);
        await db.SaveChangesAsync();
        fulfilled.SetPaymentUrl("https://payment.example.test/pay", now);
        fulfilled.MarkSucceeded(now, "901", "00", "00", PaymentProcessingStatus.Fulfilled);
        await db.SaveChangesAsync();

        var afterFailure = NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}", requestId: Guid.NewGuid());
        afterFailure.SetPaymentUrl("https://payment.example.test/pay-2", now);
        afterFailure.MarkFailed(now, "24", "02");
        db.Payments.Add(afterFailure);
        await db.SaveChangesAsync();

        var secondFulfilled = NewPayment(booking.Id, $"REF-{Guid.NewGuid():N}", requestId: Guid.NewGuid());
        secondFulfilled.SetPaymentUrl("https://payment.example.test/pay-3", now);
        secondFulfilled.MarkSucceeded(now, "902", "00", "00", PaymentProcessingStatus.Fulfilled);
        db.Payments.Add(secondFulfilled);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Vnpay_transaction_id_is_unique_when_present()
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var first = NewPayment((await CreateBookingAsync(db)).Id, $"REF-{Guid.NewGuid():N}");
        first.MarkSucceeded(now, "TX-1", "00", "00", PaymentProcessingStatus.RequiresRefundReview);
        db.Payments.Add(first);
        await db.SaveChangesAsync();

        var second = NewPayment((await CreateBookingAsync(db)).Id, $"REF-{Guid.NewGuid():N}");
        second.MarkSucceeded(now, "TX-1", "00", "00", PaymentProcessingStatus.RequiresRefundReview);
        db.Payments.Add(second);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_rejects_succeeded_payments_without_processing_status()
    {
        await using var db = fixture.CreateDbContext();
        var booking = await CreateBookingAsync(db);
        var now = DateTime.UtcNow;
        FormattableString sql = $"""
            INSERT INTO "Payments" ("Id", "BookingId", "RequestId", "MerchantReference", "Amount", "Currency", "ExpiresAtUtc", "Status", "ProcessingStatus", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {booking.Id}, {Guid.NewGuid()}, {"REF-BAD"}, {100m}, {"VND"}, {now.AddMinutes(5)}, {"Succeeded"}, {"None"}, {now})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Database_allows_one_pending_ticket_email_delivery_per_booking()
    {
        await using var db = fixture.CreateDbContext();
        var booking = await CreateBookingAsync(db);
        var now = DateTime.UtcNow;
        var pending = new TicketEmailDelivery(Guid.NewGuid(), booking.Id, now);
        db.TicketEmailDeliveries.Add(pending);
        await db.SaveChangesAsync();

        var duplicate = new TicketEmailDelivery(Guid.NewGuid(), booking.Id, now);
        db.TicketEmailDeliveries.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.Entry(duplicate).State = EntityState.Detached;

        pending.MarkSent(now);
        await db.SaveChangesAsync();
        db.TicketEmailDeliveries.Add(new TicketEmailDelivery(Guid.NewGuid(), booking.Id, now));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.TicketEmailDeliveries.CountAsync(x => x.BookingId == booking.Id));
    }

    private static Payment NewPayment(Guid bookingId, string merchantReference, Guid? requestId = null) =>
        new(Guid.NewGuid(), bookingId, requestId ?? Guid.NewGuid(), merchantReference, 120, "VND", DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow);

    private static async Task<LumiCine.Domain.Bookings.Booking> CreateBookingAsync(LumiCineDbContext db)
    {
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        return await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
    }
}
