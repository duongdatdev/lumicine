using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Payments;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.IntegrationTests.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class VnPayAcceptanceFlowTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task The_sandbox_flow_accepts_a_booking_end_to_end()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        // The showtime starts soon so the whole flow fits inside the check-in window.
        var soon = DateTime.UtcNow.AddMinutes(10);
        var showtime = new LumiCine.Domain.Scheduling.Showtime(Guid.NewGuid(), movie.Id, room.Id, soon, soon.AddMinutes(90), 100, DateTime.UtcNow);
        await db.Showtimes.AddAsync(showtime);
        await db.SaveChangesAsync();
        var seats = new Guid[2];
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            seats[i] = seat.Id;
        }
        var (client, _) = await CreateClientAsync(factory, ["Customer"]);
        var requestId = Guid.NewGuid();

        // 1. Hold the seats.
        using var bookingResponse = await client.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(requestId, showtime.Id, seats));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<BookingDetails>())!;

        // 2. Create the VNPay payment link and verify the sandbox request parameters.
        using var paymentResponse = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var link = (await paymentResponse.Content.ReadFromJsonAsync<BookingPaymentLink>())!;
        var url = link.PaymentUrl;
        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", url);
        Assert.Contains($"vnp_Amount={(long)(booking.TotalAmount * 100)}", url);
        Assert.Contains("vnp_Currency=vnd", url);
        var txnRef = Regex.Match(url, "vnp_TxnRef=([^&]+)").Groups[1].Value;
        Assert.NotEqual(string.Empty, txnRef);

        // 3. The customer sees the pending payment and the still-held seats.
        using var pendingView = await client.GetAsync($"/api/payments/{link.PaymentId}");
        Assert.Equal(HttpStatusCode.OK, pendingView.StatusCode);
        var pending = (await pendingView.Content.ReadFromJsonAsync<PaymentView>())!;
        Assert.Equal(PaymentStatus.Pending.ToString(), pending.Status);
        var seatsView = await factory.CreateClient().GetFromJsonAsync<LumiCine.Application.Scheduling.ShowtimeSeatSummary[]>(
            $"/api/showtimes/{showtime.Id}/seats");
        Assert.Equal(2, seatsView!.Count(x => x.Status == "Held"));

        // 4. VNPay confirms through the IPN callback.
        await using var paymentsDb = fixture.CreateDbContext();
        var payment = await paymentsDb.Payments.SingleAsync(x => x.Id == link.PaymentId);
        var callback = VnPayCallbacks.SignedCallbackParameters(payment.MerchantReference, (long)(payment.Amount * 100));
        using var ipn = await factory.CreateClient().GetAsync($"/api/payments/vnpay/ipn{ToQueryString(callback)}");
        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        Assert.Equal(VnPayResponseCodes.Success,
            System.Text.Json.JsonDocument.Parse(await ipn.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());

        // 5. The payment view flips to fulfilled, the booking is confirmed, seats are sold.
        using var fulfilledView = await client.GetAsync($"/api/payments/{link.PaymentId}");
        var fulfilled = (await fulfilledView.Content.ReadFromJsonAsync<PaymentView>())!;
        Assert.Equal(PaymentStatus.Succeeded.ToString(), fulfilled.Status);
        Assert.Equal(PaymentProcessingStatus.Fulfilled.ToString(), fulfilled.ProcessingStatus);
        Assert.Equal(ShowtimeStatus.Scheduled, (await paymentsDb.Showtimes.SingleAsync(x => x.Id == showtime.Id)).Status);
        await using var verifyDb = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Confirmed, (await verifyDb.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        Assert.Equal(2, await verifyDb.ShowtimeSeats.CountAsync(x => x.ShowtimeId == showtime.Id && x.Status == LumiCine.Domain.Scheduling.ShowtimeSeatStatus.Sold));

        // 6. The customer downloads the tickets and their QR codes decode to the right payloads.
        using var ticketsResponse = await client.GetAsync($"/api/bookings/{booking.Id}/tickets");
        var tickets = (await ticketsResponse.Content.ReadFromJsonAsync<IReadOnlyList<TicketDetails>>())!;
        Assert.Equal(2, tickets.Count);
        foreach (var ticket in tickets)
        {
            using var qr = await client.GetAsync($"/api/tickets/{ticket.Id}/qr");
            Assert.Equal("image/png", qr.Content.Headers.ContentType!.MediaType);
            Assert.Equal($"LCT1:{ticket.TicketCode}", DecodeQr(await qr.Content.ReadAsByteArrayAsync()));
        }

        // 7. Staff check in both tickets inside the window.
        var (staff, _) = await CreateClientAsync(factory, ["Staff"]);
        foreach (var ticket in tickets)
        {
            using var checkIn = await staff.PostAsJsonAsync("/api/staff/check-ins",
                new CheckInRequest(Guid.NewGuid(), ticket.TicketCode));
            Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
        }
        using var duplicate = await staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(Guid.NewGuid(), tickets[0].TicketCode));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // 8. The admin reviews the transaction in the reconciliation list.
        var (admin, _) = await CreateClientAsync(factory, ["Admin"]);
        using var detail = await admin.GetAsync($"/api/admin/payments/{link.PaymentId}");
        var adminView = (await detail.Content.ReadFromJsonAsync<AdminPaymentView>())!;
        Assert.Equal(PaymentProcessingStatus.Fulfilled.ToString(), adminView.Payment.ProcessingStatus);
        Assert.Equal(booking.Code, adminView.BookingCode);
    }

    [Fact]
    public async Task A_late_success_is_reviewed_and_resolved_with_a_recorded_refund()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        var customerId = Guid.NewGuid();
        var customerEmail = $"{customerId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = customerId, Email = customerEmail, NormalizedEmail = customerEmail.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Late payment"
        });
        var now = DateTime.UtcNow;
        var booking = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 100, now.AddMinutes(-2),
            holdExpiresAtUtc: now.AddMinutes(5), userId: customerId, requestId: Guid.NewGuid());
        db.Bookings.Add(booking);
        db.BookingItems.Add(new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, now));
        showtimeSeat.Hold(booking.Id);
        await db.SaveChangesAsync();
        booking.Cancel(now);
        await db.ShowtimeSeats.Where(x => x.Id == showtimeSeat.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, LumiCine.Domain.Scheduling.ShowtimeSeatStatus.Available)
                .SetProperty(x => x.CurrentBookingId, (Guid?)null));
        await db.SaveChangesAsync();
        var payment = new Payment(Guid.NewGuid(), booking.Id, Guid.NewGuid(),
            $"LC{booking.Id.ToString("N")[..32]}.{Guid.NewGuid().ToString("N")[..12]}", 100, "VND",
            booking.HoldExpiresAtUtc!.Value, now);
        payment.SetPaymentUrl($"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef={payment.MerchantReference}", now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // The customer paid anyway and VNPay confirms the transaction after the cancellation.
        var callback = VnPayCallbacks.SignedCallbackParameters(payment.MerchantReference, (long)(payment.Amount * 100));
        using var ipn = await factory.CreateClient().GetAsync($"/api/payments/vnpay/ipn{ToQueryString(callback)}");
        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);

        var (admin, _) = await CreateClientAsync(factory, ["Admin"]);
        using var reviewList = await admin.GetAsync($"/api/admin/payments?processingStatus=RequiresRefundReview&bookingId={booking.Id}");
        var reviews = (await reviewList.Content.ReadFromJsonAsync<LumiCine.Application.Common.PagedResult<AdminPaymentView>>())!;
        Assert.Equal(1, reviews.TotalCount);

        using var refundResponse = await admin.PostAsJsonAsync($"/api/admin/payments/{payment.Id}/record-refund",
            new RecordRefundRequest("SANDBOX-REFUND-1", "Returned to the sandbox card"));
        Assert.Equal(HttpStatusCode.OK, refundResponse.StatusCode);
        var refunded = (await refundResponse.Content.ReadFromJsonAsync<AdminPaymentView>())!;
        Assert.Equal(PaymentProcessingStatus.RefundRecorded.ToString(), refunded.Payment.ProcessingStatus);

        using var recordedList = await admin.GetAsync($"/api/admin/payments?processingStatus=RefundRecorded&bookingId={booking.Id}");
        var recorded = (await recordedList.Content.ReadFromJsonAsync<LumiCine.Application.Common.PagedResult<AdminPaymentView>>())!;
        Assert.Equal(1, recorded.TotalCount);
        Assert.Equal(PaymentProcessingStatus.RefundRecorded.ToString(), recorded.Items.Single().Payment.ProcessingStatus);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal("SANDBOX-REFUND-1", (await verify.Payments.SingleAsync(x => x.Id == payment.Id)).RefundReference);
        var lateItemIds = await verify.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
        Assert.Equal(0, await verify.Tickets.CountAsync(x => lateItemIds.Contains(x.BookingItemId)));
        // Cancellation audit: the booking stays cancelled.
        Assert.Equal(BookingStatus.Cancelled, (await verify.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
    }

    private static string ToQueryString(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters) => "?" + string.Join("&",
        parameters.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value.Single())}"));

    private static string DecodeQr(byte[] png)
    {
        using var bitmap = SkiaSharp.SKBitmap.Decode(png);
        using var pixmap = bitmap.PeekPixels()!;
        var span = pixmap.GetPixelSpan<byte>();
        var bpp = pixmap.Info.BytesPerPixel;
        var luminances = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var offset = y * pixmap.RowBytes + x * bpp;
                luminances[y * bitmap.Width + x] = pixmap.ColorType switch
                {
                    SkiaSharp.SKColorType.Gray8 => span[offset],
                    SkiaSharp.SKColorType.Rgba8888 => (byte)(0.30 * span[offset] + 0.59 * span[offset + 1] + 0.11 * span[offset + 2]),
                    SkiaSharp.SKColorType.Bgra8888 => (byte)(0.30 * span[offset + 2] + 0.59 * span[offset + 1] + 0.11 * span[offset]),
                    _ => throw new InvalidOperationException($"Unsupported pixel format {pixmap.ColorType}.")
                };
            }
        }
        var luminance = new RGBLuminanceSource(luminances, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8);
        var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(luminance)));
        Assert.NotNull(result);
        return result!.Text;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string[] roles)
    {
        await using var db = fixture.CreateDbContext();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Acceptance flow"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, roles).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }
}
