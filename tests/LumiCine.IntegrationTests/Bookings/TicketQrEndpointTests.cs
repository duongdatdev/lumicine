using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Bookings;
using LumiCine.IntegrationTests.Bookings;
using Microsoft.EntityFrameworkCore;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.IntegrationTests.Payments;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class TicketQrEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public void Renderer_encodes_the_ticket_code_with_a_quiet_zone()
    {
        var renderer = new TicketQrRenderer();
        var code = new string('A', 43);

        var png = renderer.RenderPng(code);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        using var bitmap = SKBitmap.Decode(png);
        Assert.True(bitmap.Width >= 200);
        Assert.True(bitmap.Height >= 200);
        // The QR spec quiet zone is four light modules on every side.
        Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1));
        Assert.Equal(SKColors.White, bitmap.GetPixel(4 * 8 - 1, 4 * 8 - 1));
    }

    [Fact]
    public async Task Qr_image_decodes_back_to_the_ticket_payload_for_the_owner_only()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        var (foreign, _) = await CreateClientAsync(factory);
        using var list = await client.GetAsync($"/api/bookings/{booking.Id}/tickets");
        var tickets = (await list.Content.ReadFromJsonAsync<IReadOnlyList<TicketDetails>>())!;

        using var qr = await client.GetAsync($"/api/tickets/{tickets[0].Id}/qr");
        using var foreignQr = await foreign.GetAsync($"/api/tickets/{tickets[0].Id}/qr");
        using var anonymous = await factory.CreateClient().GetAsync($"/api/tickets/{tickets[0].Id}/qr");

        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.True(qr.Headers.CacheControl?.NoStore);
        Assert.Equal("image/png", qr.Content.Headers.ContentType!.MediaType);
        var png = await qr.Content.ReadAsByteArrayAsync();
        Assert.Equal($"LCT1:{tickets[0].TicketCode}", Decode(png));
        Assert.Equal(HttpStatusCode.NotFound, foreignQr.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Qr_images_of_two_tickets_differ()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        using var list = await client.GetAsync($"/api/bookings/{booking.Id}/tickets");
        var tickets = (await list.Content.ReadFromJsonAsync<IReadOnlyList<TicketDetails>>())!;

        var first = await (await client.GetAsync($"/api/tickets/{tickets[0].Id}/qr")).Content.ReadAsByteArrayAsync();
        var second = await (await client.GetAsync($"/api/tickets/{tickets[1].Id}/qr")).Content.ReadAsByteArrayAsync();

        Assert.NotEqual(first, second);
    }

    private static string Decode(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        using var pixmap = bitmap.PeekPixels();
        Assert.True(pixmap is not null);
        var bpp = pixmap.Info.BytesPerPixel;
        var span = pixmap.GetPixelSpan<byte>();
        var luminances = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var offset = y * pixmap.RowBytes + x * bpp;
                luminances[y * bitmap.Width + x] = pixmap.ColorType switch
                {
                    SKColorType.Gray8 => span[offset],
                    SKColorType.Rgba8888 => (byte)(0.30 * span[offset] + 0.59 * span[offset + 1] + 0.11 * span[offset + 2]),
                    SKColorType.Bgra8888 => (byte)(0.30 * span[offset + 2] + 0.59 * span[offset + 1] + 0.11 * span[offset]),
                    _ => throw new InvalidOperationException($"Unsupported pixel format {pixmap.ColorType}.")
                };
            }
        }
        var luminance = new RGBLuminanceSource(luminances, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8);
        var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(luminance)));
        Assert.NotNull(result);
        return result!.Text;
    }

    private async Task<(HttpClient Client, LumiCine.Application.Bookings.BookingDetails Booking)> CreateFulfilledBookingAsync(
        ApiWebApplicationFactory factory)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var seats = new Guid[2];
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            seats[i] = seat.Id;
        }
        var (client, _) = await CreateClientAsync(factory);
        using var bookingResponse = await client.PostAsJsonAsync("/api/bookings", new LumiCine.Application.Bookings.CreateBookingRequest(Guid.NewGuid(), showtime.Id, seats));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<LumiCine.Application.Bookings.BookingDetails>())!;
        using var paymentResponse = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new LumiCine.Application.Payments.CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var link = (await paymentResponse.Content.ReadFromJsonAsync<LumiCine.Application.Payments.BookingPaymentLink>())!;
        var txnRef = Regex.Match(link.PaymentUrl, "vnp_TxnRef=([^&]+)").Groups[1].Value;
        await using var paymentsDb = fixture.CreateDbContext();
        var payment = await paymentsDb.Payments.SingleAsync(x => x.Id == link.PaymentId);
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment.MerchantReference, (long)(payment.Amount * 100));
        using var scope = factory.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<LumiCine.Application.Payments.IPaymentService>();
        var outcome = await payments.ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
        Assert.Equal(LumiCine.Application.Payments.VnPayResponseCodes.Success, outcome.RspCode);
        return (client, booking);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        await using var db = fixture.CreateDbContext();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "QR test"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, ["Customer"]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }
}
