using System.Security.Cryptography;
using System.Text;
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
    private const string HashSecret = "test-secret-value-0123456789";
    private const string TmnCode = "LUMITEST";

    [Fact]
    public async Task Invalid_signatures_are_rejected_before_any_lookup()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        var parameters = SignedCallbackParameters(payment);
        parameters["vnp_Amount"] = ["99900"];

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.InvalidSignature, outcome.RspCode);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public async Task Unknown_transaction_references_are_reported_as_not_found()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var parameters = SignedCallbackParameters(merchantReference: "LCUNKNOWN.000000000000", amountMinor: 20000);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.TransactionNotFound, outcome.RspCode);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public async Task Amount_mismatches_are_rejected_as_invalid_amount()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        var parameters = SignedCallbackParameters(payment, amountMinor: (long)(payment.Amount * 100) + 100);

        var outcome = await ApplyCallbackAsync(factory, parameters);

        Assert.Equal(VnPayResponseCodes.InvalidAmount, outcome.RspCode);
        Assert.Equal(payment.Id, outcome.Payment!.Id);
    }

    [Fact]
    public async Task Failure_results_mark_the_payment_failed_and_confirm_receipt()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (payment, _) = await InsertPendingPaymentAsync();
        var parameters = SignedCallbackParameters(payment, responseCode: "24", transactionStatus: "02");

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
        var parameters = SignedCallbackParameters(payment, responseCode: "24", transactionStatus: "02");

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
        var parameters = SignedCallbackParameters(payment, responseCode: "24", transactionStatus: "02");

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
        stored.MarkSucceeded(DateTime.UtcNow, "987", "00", "00", PaymentProcessingStatus.Fulfilled);
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

    private static Dictionary<string, IReadOnlyList<string>> SignedCallbackParameters(
        Payment payment,
        long? amountMinor = null,
        string responseCode = "00",
        string transactionStatus = "00",
        Action<Dictionary<string, string>>? fields = null) =>
        SignedCallbackParameters(payment.MerchantReference, amountMinor ?? (long)(payment.Amount * 100), responseCode, transactionStatus, fields);

    private static Dictionary<string, IReadOnlyList<string>> SignedCallbackParameters(
        string merchantReference,
        long amountMinor,
        string responseCode = "00",
        string transactionStatus = "00",
        Action<Dictionary<string, string>>? fields = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = amountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["vnp_BankCode"] = "NCB",
            ["vnp_Command"] = "pay",
            ["vnp_CreateDate"] = "20261113204500",
            ["vnp_Currency"] = "vnd",
            ["vnp_IpAddr"] = "127.0.0.1",
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = "Ve xem phim",
            ["vnp_OrderType"] = "other",
            ["vnp_PayDate"] = "20261113204600",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TmnCode"] = TmnCode,
            ["vnp_TransactionNo"] = "14103428",
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_TxnRef"] = merchantReference,
            ["vnp_Version"] = "2.1.0"
        };
        fields?.Invoke(values);
        var query = string.Join("&", values.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HashSecret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        var parameters = values.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[p.Value]);
        parameters["vnp_SecureHash"] = [hash];
        return parameters;
    }
}
