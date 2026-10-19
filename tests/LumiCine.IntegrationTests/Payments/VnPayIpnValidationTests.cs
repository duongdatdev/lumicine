using LumiCine.Application.Payments;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Cinemas;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class VnPayIpnValidationTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Invalid_signatures_are_rejected_before_any_lookup()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment);
        parameters["vnp_Amount"] = ["99900"];

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.InvalidSignature, outcome.RspCode);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public async Task Unknown_transaction_references_are_reported_as_not_found()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var parameters = VnPayCallbacks.SignedCallbackParameters(merchantReference: "LCUNKNOWN.000000000000", amountMinor: 20000);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.TransactionNotFound, outcome.RspCode);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public async Task Amount_mismatches_are_rejected_as_invalid_amount()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment, amountMinor: (long)(payment.Amount * 100) + 100);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.InvalidAmount, outcome.RspCode);
        Assert.Equal(payment.Id, outcome.Payment!.Id);
    }

    [Fact]
    public async Task Failure_results_mark_the_payment_failed_and_confirm_receipt()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment, responseCode: "24", transactionStatus: "02");

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
        Assert.Equal(PaymentStatus.Failed.ToString(), outcome.Payment!.Status);
        Assert.Equal("24", outcome.Payment.ResponseCode);
        Assert.Equal("02", outcome.Payment.TransactionStatus);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Payments.SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, stored.Status);
        Assert.Equal(PaymentProcessingStatus.None, stored.ProcessingStatus);
        Assert.NotNull(stored.ProcessedAtUtc);
    }

    [Fact]
    public async Task Repeated_failure_results_report_the_order_as_already_processed()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, booking) = await InsertPendingPaymentAsync();
        MarkPaymentFailed(payment);
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment, responseCode: "24", transactionStatus: "02");

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.AlreadyProcessed, outcome.RspCode);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Payments.SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, stored.Status);
        Assert.Equal(booking.Id, stored.BookingId);
    }

    [Fact]
    public async Task Failure_callbacks_after_a_confirmed_success_do_not_downgrade_the_payment()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        MarkPaymentSucceeded(payment);
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment, responseCode: "24", transactionStatus: "02");

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.AlreadyProcessed, outcome.RspCode);
        await using var db = fixture.CreateDbContext();
        var stored = await db.Payments.SingleAsync(x => x.Id == payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, stored.Status);
        Assert.Equal(PaymentProcessingStatus.Fulfilled, stored.ProcessingStatus);
    }

    private static async Task<VnPayCallbackOutcome> ApplyCallbackAsync(
        ApiWebApplicationFactory factory, IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        using var scope = factory.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        return await payments.ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
    }

    private void MarkPaymentFailed(Payment payment)
    {
        using var db = fixture.CreateDbContext();
        var stored = db.Payments.Single(x => x.Id == payment.Id);
        stored.MarkFailed(DateTime.UtcNow, "24", "02");
        db.SaveChanges();
    }

    private void MarkPaymentSucceeded(Payment payment)
    {
        using var db = fixture.CreateDbContext();
        var stored = db.Payments.Single(x => x.Id == payment.Id);
        stored.MarkSucceeded(DateTime.UtcNow, stored.Id.ToString("N")[..16], "00", "00", PaymentProcessingStatus.Fulfilled);
        db.SaveChanges();
    }

    private async Task<(Payment Payment, LumiCine.Domain.Bookings.Booking Booking)> InsertPendingPaymentAsync()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var now = DateTime.UtcNow;
        var booking = new LumiCine.Domain.Bookings.Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 200, now,
            holdExpiresAtUtc: now.AddMinutes(5));
        db.Bookings.Add(booking);
        for (var i = 0; i < 2; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            db.BookingItems.Add(new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, now));
            showtimeSeat.Hold(booking.Id);
        }
        var payment = new Payment(Guid.NewGuid(), booking.Id, Guid.NewGuid(),
            $"LC{booking.Id.ToString("N")[..32]}.{Guid.NewGuid().ToString("N")[..12]}", 200, "VND",
            booking.HoldExpiresAtUtc!.Value, now);
        payment.SetPaymentUrl($"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef={payment.MerchantReference}", now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (payment, booking);
    }
}
