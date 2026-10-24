using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Common;
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
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, paymentId, bookingId) = await SetupAsync(
            factory, fake, new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));

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
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("24", "Transaction declined", "778899", "02", 20000));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, paymentId, _) = await SetupAsync(
            factory, fake, new VnPayTransactionQueryResult("24", "Transaction declined", "778899", "02", 20000));

        using var response = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<VnPayReconcileResult>())!;
        Assert.Equal(VnPayResponseCodes.Success, result.RspCode);
        Assert.Equal(PaymentStatus.Failed.ToString(), result.Payment.Status);
    }

    [Fact]
    public async Task Reconcile_reports_amount_mismatches_without_applying()
    {
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000 + 500));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, paymentId, bookingId) = await SetupAsync(
            factory, fake, new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000 + 500));

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
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, paymentId, _) = await SetupAsync(factory, fake, new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var first = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await admin.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);

        var result = (await second.Content.ReadFromJsonAsync<VnPayReconcileResult>())!;
        Assert.Equal(VnPayResponseCodes.AlreadyProcessed, result.RspCode);
        Assert.Equal(2, fake.Queries.Count);
    }

    [Fact]
    public async Task Admin_records_the_manual_refund_for_a_reviewed_payment()
    {
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, paymentId, _) = await SetupAsync(
            factory, fake, new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        await using (var db = fixture.CreateDbContext())
        {
            var payment = await db.Payments.FindAsync(paymentId);
            payment!.SetPaymentUrl("https://payment.example.test/x", DateTime.UtcNow);
            payment.MarkSucceeded(DateTime.UtcNow, Guid.NewGuid().ToString("N")[..16], "00", "00", PaymentProcessingStatus.RequiresRefundReview);
            await db.SaveChangesAsync();
        }

        using var missing = await admin.PostAsJsonAsync($"/api/admin/payments/{Guid.NewGuid()}/record-refund",
            new RecordRefundRequest("BANK-REF-1", "Returned to source card"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var response = await admin.PostAsJsonAsync($"/api/admin/payments/{paymentId}/record-refund",
            new RecordRefundRequest("BANK-REF-1", "Returned to source card"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = (await response.Content.ReadFromJsonAsync<AdminPaymentView>())!;
        Assert.Equal(PaymentProcessingStatus.RefundRecorded.ToString(), view.Payment.ProcessingStatus);

        await using var verify = fixture.CreateDbContext();
        var stored = await verify.Payments.SingleAsync(x => x.Id == paymentId);
        Assert.Equal("BANK-REF-1", stored.RefundReference);
        Assert.Equal("Returned to source card", stored.RefundNote);
        Assert.NotNull(stored.RefundRecordedAtUtc);
        Assert.NotNull(stored.RefundRecordedByUserId);

        using var repeated = await admin.PostAsJsonAsync($"/api/admin/payments/{paymentId}/record-refund",
            new RecordRefundRequest("BANK-REF-2", null));
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        var (pendingMovie, _, pendingRoom) = await DatabaseTestData.CreateBaseAsync(verify);
        var pendingShowtime = await DatabaseTestData.CreateShowtimeAsync(verify, pendingMovie.Id, pendingRoom.Id);
        var pendingBooking = await DatabaseTestData.CreateBookingAsync(verify, pendingShowtime.Id);
        var pendingPayment = new Payment(Guid.NewGuid(), pendingBooking.Id, Guid.NewGuid(), $"REF-{Guid.NewGuid():N}", 200, "VND",
            DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow);
        pendingPayment.SetPaymentUrl("https://payment.example.test/z", DateTime.UtcNow);
        verify.Payments.Add(pendingPayment);
        await verify.SaveChangesAsync();
        using var pendingRefund = await admin.PostAsJsonAsync($"/api/admin/payments/{pendingPayment.Id}/record-refund",
            new RecordRefundRequest("BANK-REF-3", null));
        Assert.Equal(HttpStatusCode.Conflict, pendingRefund.StatusCode);

        using var noReference = await admin.PostAsJsonAsync($"/api/admin/payments/{paymentId}/record-refund",
            new RecordRefundRequest(" ", null));
        Assert.Equal(HttpStatusCode.BadRequest, noReference.StatusCode);
    }

    [Fact]
    public async Task Admin_payment_queries_list_and_filter_transactions()
    {
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, pendingId, bookingId) = await SetupAsync(
            factory, fake, new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        await using (var db = fixture.CreateDbContext())
        {
            // A reviewed transaction to filter for.
            var reviewed = await db.Payments.FindAsync(pendingId);
            reviewed!.SetPaymentUrl("https://payment.example.test/x", DateTime.UtcNow);
            reviewed.MarkSucceeded(DateTime.UtcNow, Guid.NewGuid().ToString("N")[..16], "00", "00", PaymentProcessingStatus.RequiresRefundReview);
            var failed = new Payment(Guid.NewGuid(), reviewed.BookingId, Guid.NewGuid(), $"REF-{Guid.NewGuid():N}", 200, "VND",
                DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow);
            failed.SetPaymentUrl("https://payment.example.test/y", DateTime.UtcNow);
            failed.MarkFailed(DateTime.UtcNow, "24", "02");
            db.Payments.Add(failed);
            await db.SaveChangesAsync();
        }

        using var list = await admin.GetAsync($"/api/admin/payments?processingStatus=RequiresRefundReview&bookingId={bookingId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        var reviewedPage = (await list.Content.ReadFromJsonAsync<PagedResult<AdminPaymentView>>())!;
        Assert.Equal(1, reviewedPage.TotalCount); // the reviewed booking owns exactly one RequiresRefundReview payment
        Assert.Equal(PaymentProcessingStatus.RequiresRefundReview.ToString(), reviewedPage.Items.Single().Payment.ProcessingStatus);
        Assert.NotEqual(string.Empty, reviewedPage.Items.Single().BookingCode);
        Assert.Contains("@example.com", reviewedPage.Items.Single().CustomerEmail);

        using var bookingFilter = await admin.GetAsync($"/api/admin/payments?status=Failed&bookingId={bookingId}");
        var failedPage = (await bookingFilter.Content.ReadFromJsonAsync<PagedResult<AdminPaymentView>>())!;
        Assert.Equal(1, failedPage.TotalCount);
        Assert.Equal(PaymentStatus.Failed.ToString(), failedPage.Items.Single().Payment.Status);

        using var invalid = await admin.GetAsync("/api/admin/payments?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var detail = await admin.GetAsync($"/api/admin/payments/{pendingId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var view = (await detail.Content.ReadFromJsonAsync<AdminPaymentView>())!;
        Assert.Equal(pendingId, view.Payment.Id);
        using var missing = await admin.GetAsync($"/api/admin/payments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var (customer, customerId) = await CreateClientAsync(factory, ["Customer"]);
        _ = customerId;
        using var forbidden = await customer.GetAsync("/api/admin/payments");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var unauthorized = await factory.CreateClient().GetAsync("/api/admin/payments");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Reconcile_requires_an_admin_and_a_known_payment()
    {
        var fake = new FakeQueryClient(new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));
        using var factory = CreateReconcileFactory(fake);
        var (admin, _, paymentId, _) = await SetupAsync(
            factory, fake, new VnPayTransactionQueryResult("00", "Confirm Success", "778899", "00", 20000));

        using var anonymous = await factory.CreateClient().PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var (customer, customerId) = await CreateClientAsync(factory, ["Customer"]);
        using var forbidden = await customer.PostAsync($"/api/admin/payments/{paymentId}/reconcile", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var missing = await admin.PostAsync($"/api/admin/payments/{Guid.NewGuid()}/reconcile", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateReconcileFactory(FakeQueryClient fake) =>
        new ApiWebApplicationFactory(connectionString: fixture.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IVnPayTransactionQueryClient>(fake))));

    private async Task<(HttpClient AdminClient, FakeQueryClient QueryClient, Guid PaymentId, Guid BookingId)> SetupAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, FakeQueryClient fake, VnPayTransactionQueryResult result)
    {
        // Every test shares the collection database, so recorded VNPay transaction ids must not collide.
        fake.Result = result = result with { TransactionNo = Guid.NewGuid().ToString("N")[..16] };

        await using (var db = fixture.CreateDbContext())
        {
            var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
            var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
            var now = DateTime.UtcNow;
            var customerId = Guid.NewGuid();
            var customerEmail = $"{customerId:N}@example.com";
            db.Users.Add(new LumiCine.Infrastructure.Identity.ApplicationUser
            {
                Id = customerId, Email = customerEmail, NormalizedEmail = customerEmail.ToUpperInvariant(),
                EmailConfirmed = true, IsActive = true, DisplayName = "Review test"
            });
            var booking = new LumiCine.Domain.Bookings.Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 200, now,
                holdExpiresAtUtc: now.AddMinutes(5), userId: customerId, requestId: Guid.NewGuid());
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
            var (admin, _) = await CreateClientAsync(factory, ["Admin"]);
            return (admin, fake, payment.Id, booking.Id);
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

        public VnPayTransactionQueryResult Result { get; set; } = result;

        public Task<VnPayTransactionQueryResult> QueryAsync(string transactionReference, decimal amount, DateTime transactionDateUtc, CancellationToken cancellationToken)
        {
            Queries.Add(transactionReference);
            return Task.FromResult(Result);
        }
    }
}
