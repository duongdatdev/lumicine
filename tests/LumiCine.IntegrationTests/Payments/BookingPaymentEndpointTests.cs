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
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class BookingPaymentEndpointTests(PostgresDatabaseFixture fixture)
{
    private const string SandboxBaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    [Fact]
    public async Task Payment_link_is_idempotent_and_replays_the_same_url()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, _) = await CreateClientAsync(factory);
        var (showtime, seats) = await CreateShowtimeAsync();
        var booking = await CreateBookingAsync(client, showtime, seats[..2]);
        var requestId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay", new CreateBookingPaymentRequest(requestId));
        var second = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay", new CreateBookingPaymentRequest(requestId));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstLink = (await first.Content.ReadFromJsonAsync<BookingPaymentLink>())!;
        var secondLink = (await second.Content.ReadFromJsonAsync<BookingPaymentLink>())!;

        Assert.Equal(firstLink.PaymentId, secondLink.PaymentId);
        Assert.Equal(firstLink.PaymentUrl, secondLink.PaymentUrl);
        Assert.Equal(requestId, firstLink.RequestId);
        Assert.StartsWith(SandboxBaseUrl, firstLink.PaymentUrl);
        Assert.Contains("vnp_TxnRef=", firstLink.PaymentUrl);
        Assert.Equal(booking.TotalAmount * 100, long.Parse(Regex.Match(firstLink.PaymentUrl, "vnp_Amount=(\\d+)").Groups[1].Value));
        Assert.Matches("^LC[0-9a-f]{32}\\.[0-9a-f]{12}$", Regex.Match(firstLink.PaymentUrl, "vnp_TxnRef=([^&]+)").Groups[1].Value);
        Assert.True(first.Headers.CacheControl?.NoStore);

        await using var db = fixture.CreateDbContext();
        var payment = await db.Payments.SingleAsync(x => x.Id == firstLink.PaymentId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(booking.Id, payment.BookingId);
        Assert.Equal("VND", payment.Currency);
        Assert.Equal(booking.HoldExpiresAtUtc, payment.ExpiresAtUtc);
        Assert.Equal(firstLink.PaymentUrl, payment.PaymentUrl);
    }

    [Fact]
    public async Task Zero_total_bookings_cannot_be_paid_online()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, userId) = await CreateClientAsync(factory);
        var booking = await InsertBookingAsync(userId, totalAmount: 0);

        using var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bookings_without_a_hold_deadline_are_rejected()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, userId) = await CreateClientAsync(factory);
        var booking = await InsertBookingAsync(userId, withHold: false);

        using var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Expired_and_cancelled_bookings_are_rejected()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, userId) = await CreateClientAsync(factory);
        var expired = await InsertBookingAsync(userId, holdDuration: TimeSpan.FromMinutes(-1));
        using var expiredResponse = await client.PostAsJsonAsync($"/api/bookings/{expired.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, expiredResponse.StatusCode);

        var cancelled = await InsertBookingAsync(userId, customize: booking => booking.Cancel(DateTime.UtcNow));
        using var cancelledResponse = await client.PostAsJsonAsync($"/api/bookings/{cancelled.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, cancelledResponse.StatusCode);
    }

    [Fact]
    public async Task Bookings_of_other_accounts_are_not_found()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, _) = await CreateClientAsync(factory);
        var (otherClient, otherUserId) = await CreateClientAsync(factory);
        var booking = await InsertBookingAsync(otherUserId);

        using var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unauthenticated = factory.CreateClient();
        using var unauthorized = await unauthenticated.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Failed_payments_allow_a_new_attempt()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, userId) = await CreateClientAsync(factory);
        var booking = await InsertBookingAsync(userId);
        var requestId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            var failed = new Payment(Guid.NewGuid(), booking.Id, requestId, $"REF-{Guid.NewGuid():N}", 200, "VND",
                booking.HoldExpiresAtUtc!.Value, now);
            failed.SetPaymentUrl($"{SandboxBaseUrl}?vnp_TxnRef=REF-FAILED", now);
            failed.MarkFailed(now, "24", "02");
            db.Payments.Add(failed);
            await db.SaveChangesAsync();
        }

        using var retry = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(requestId));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryLink = (await retry.Content.ReadFromJsonAsync<BookingPaymentLink>())!;
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(2, await db.Payments.CountAsync(x => x.BookingId == booking.Id));
            Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(x => x.Id == retryLink.PaymentId)).Status);
            Assert.All(await db.Payments.Where(x => x.BookingId == booking.Id && x.Id != retryLink.PaymentId).ToListAsync(),
                x => Assert.Equal(PaymentStatus.Failed, x.Status));
        }
    }

    [Fact]
    public async Task Succeeded_payments_block_new_payment_attempts()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, userId) = await CreateClientAsync(factory);
        var booking = await InsertBookingAsync(userId);
        var requestId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            var succeeded = new Payment(Guid.NewGuid(), booking.Id, requestId, $"REF-{Guid.NewGuid():N}", 200, "VND",
                booking.HoldExpiresAtUtc!.Value, now);
            succeeded.MarkSucceeded(now, "77", "00", "00", PaymentProcessingStatus.RequiresRefundReview);
            db.Payments.Add(succeeded);
            await db.SaveChangesAsync();
        }

        using var sameRequest = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(requestId));
        using var newRequest = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Conflict, sameRequest.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, newRequest.StatusCode);
    }

    [Fact]
    public async Task Payments_require_vnpay_configuration()
    {
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["VnPay:TmnCode"] = "" })));
        var (client, userId) = await CreateClientAsync(factory);
        var booking = await InsertBookingAsync(userId);

        using var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private async Task<LumiCine.Domain.Bookings.Booking> InsertBookingAsync(
        Guid userId,
        decimal totalAmount = 200,
        bool withHold = true,
        TimeSpan holdDuration = default,
        Action<LumiCine.Domain.Bookings.Booking>? customize = null)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var now = DateTime.UtcNow;
        DateTime? hold = !withHold ? null : now.Add(holdDuration == default ? TimeSpan.FromMinutes(5) : holdDuration);
        var createdAt = (hold ?? now).AddMinutes(-1);
        var booking = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, totalAmount, createdAt,
            holdExpiresAtUtc: hold, userId: userId);
        customize?.Invoke(booking);
        db.Bookings.Add(booking);
        for (var i = 0; i < 2; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            db.BookingItems.Add(new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, showtimeSeat.Price, createdAt));
            if (booking.Status == BookingStatus.Pending && hold is DateTime expiry && expiry > now)
                showtimeSeat.Hold(booking.Id);
        }
        await db.SaveChangesAsync();
        return booking;
    }

    private static async Task<BookingDetails> CreateBookingAsync(HttpClient client, Guid showtimeId, IReadOnlyList<Guid> seats)
    {
        using var response = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(Guid.NewGuid(), showtimeId, seats));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingDetails>())!;
    }

    private async Task<(Guid ShowtimeId, Guid[] Seats)> CreateShowtimeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var seats = new Guid[3];
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            seats[i] = seat.Id;
        }
        return (showtime.Id, seats);
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
            EmailConfirmed = true, IsActive = true, DisplayName = "Payment test"
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
