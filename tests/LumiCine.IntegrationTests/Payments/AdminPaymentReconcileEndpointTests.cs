using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Payments;
using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AdminPaymentReconcileEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Reconcile_confirms_a_paid_transaction_and_issues_the_tickets()
    {
        var (factory, admin, _, paymentId, bookingId) = await SetupAsync(
            new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));

        using var response = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VnPayReconcileResult>())!;
        Assert.Equal(VnPayResponseCodes.Success, result.RspCode);
        Assert.Equal(PaymentProcessingStatus.Fulfilled.ToString(), result.Payment.ProcessingStatus);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(x => x.Id == bookingId)).Status);
        var itemIds = await db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).ToListAsync();
        Assert.Equal(itemIds.Count, await db.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
    }

    [Fact]
    public async Task Reconcile_records_a_failed_transaction()
    {
        var (factory, admin, _, paymentId, _) = await SetupAsync(
            new VnPayTransactionQueryResult("24", "Transaction declined", "778899", "02", 20000));

        using var response = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VnPayReconcileResult>())!;
        Assert.Equal(VnPayResponseCodes.Success, result.RspCode);
        Assert.Equal(PaymentStatus.Failed.ToString(), result.Payment.Status);
    }

    [Fact]
    public async Task Reconcile_reports_amount_mismatches_without_applying()
    {
        var (factory, admin, _, paymentId, bookingId) = await SetupAsync(
            new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000 + 500));

        using var response = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VnPayReconcileResult>())!;
        Assert.Equal(VnPayResponseCodes.InvalidAmount, result.RspCode);
        Assert.Equal(PaymentStatus.Pending.ToString(), result.Payment.Status);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Pending, (await db.Bookings.SingleAsync(x => x.Id == bookingId)).Status);
    }

    [Fact]
    public async Task Reconcile_replays_report_the_recorded_outcome()
    {
        var (factory, admin, fake, paymentId, _) = await SetupAsync(
            new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var first = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);

        var result = (await second.Content.ReadFromJsonAsync<VnPayReconcileResult>())!;
        Assert.Equal(VnPayResponseCodes.AlreadyProcessed, result.RspCode);
        Assert.Equal(2, fake.Queries.Count);
    }

    [Fact]
    public async Task Reconcile_requires_an_admin_and_a_known_payment()
    {
        var (factory, admin, _, paymentId, _) = await SetupAsync(
            new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));

        using var anonymous = await factory.CreateClient().PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var (customer, _) = await CreateClientAsync(factory, ["Customer"]);
        using var forbidden = await customer.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var missing = await admin.PostAsync($"/api/admin/payments/{Guid.NewGuid()}/reconcile", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, HttpClient AdminClient, FakeQueryClient QueryClient, Guid PaymentId, Guid BookingId)> SetupAsync(
        VnPayTransactionQueryResult result)
    {
        // Every test shares the collection database, so recorded VNPay transaction ids must not collide.
        result = result with { TransactionNo = Guid.NewGuid().ToString("N")[..16] };
        var fake = new FakeQueryClient(result);
        var configured = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IVnPayTransactionQueryClient>(fake))));

        await using (var db = fixture.CreateDbContext())
        {
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
            var (admin, _) = await CreateClientAsync(configured, ["Admin"]);
            return (configured, admin, fake, payment.Id, booking.Id);
        }
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
            EmailConfirmed = true, IsActive = true, DisplayName = "Reconcile test"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, roles).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    internal sealed class FakeQueryClient(VnPayTransactionQueryResult result) : IVnPayTransactionQueryClient
    {
        public List<string> Queries { get; } = [];

        public Task<VnPayTransactionQueryResult> QueryAsync(string transactionReference, decimal amount, DateTime transactionDateUtc, CancellationToken cancellationToken)
        {
            Queries.Add(transactionReference);
            return Task.FromResult(result);
        }
    }
}
