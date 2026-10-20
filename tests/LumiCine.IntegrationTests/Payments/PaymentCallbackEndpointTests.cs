using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Payments;
using LumiCine.API.Controllers;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Payments;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class PaymentCallbackEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Payment_status_is_visible_only_to_its_owner()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (_, _, _, _, paymentId, client) = await CreateBookingWithPaymentAsync(factory);
        var (foreign, _) = await CreateClientAsync(factory);

        using var own = await client.GetAsync($"/api/payments/{paymentId}");
        using var other = await foreign.GetAsync($"/api/payments/{paymentId}");
        using var anonymous = await factory.CreateClient().GetAsync($"/api/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.True(own.Headers.CacheControl?.NoStore);
        var view = (await own.Content.ReadFromJsonAsync<PaymentView>())!;
        Assert.Equal(PaymentStatus.Pending.ToString(), view.Status);
        Assert.Equal(PaymentProcessingStatus.None.ToString(), view.ProcessingStatus);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Return_before_ipn_fulfills_once_and_the_ipn_replay_is_reported()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (bookingId, txnRef, amountMinor, showtimeId, _, _) = await CreateBookingWithPaymentAsync(factory);
        var parameters = VnPayCallbacks.SignedCallbackParameters(txnRef, amountMinor);

        using var returned = await factory.CreateClient().GetAsync($"/api/payments/vnpay/return{ToQueryString(parameters)}");
        using var ipn = await factory.CreateClient().GetAsync($"/api/payments/vnpay/ipn{ToQueryString(parameters)}");

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        var returnResult = (await returned.Content.ReadFromJsonAsync<VnPayReturnResponse>())!;
        Assert.Equal(VnPayResponseCodes.Success, returnResult.RspCode);
        Assert.Equal(PaymentProcessingStatus.Fulfilled.ToString(), returnResult.Payment!.ProcessingStatus);
        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        Assert.Equal(VnPayResponseCodes.AlreadyProcessed, JsonDocument.Parse(await ipn.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(x => x.Id == bookingId)).Status);
        var itemIds = await db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).ToListAsync();
        Assert.Equal(itemIds.Count, await db.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
        Assert.Equal(1, await db.TicketEmailDeliveries.CountAsync(x => x.BookingId == bookingId));
        Assert.All(await db.ShowtimeSeats.Where(x => x.ShowtimeId == showtimeId && x.CurrentBookingId == bookingId).ToListAsync(),
            seat => Assert.Equal(ShowtimeSeatStatus.Sold, seat.Status));
    }

    [Fact]
    public async Task Ipn_before_return_confirms_and_the_return_reports_the_current_status()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (bookingId, txnRef, amountMinor, _, _, _) = await CreateBookingWithPaymentAsync(factory);
        var parameters = VnPayCallbacks.SignedCallbackParameters(txnRef, amountMinor);
        var http = factory.CreateClient();

        using var ipn = await http.GetAsync($"/api/payments/vnpay/ipn{ToQueryString(parameters)}");
        using var returned = await http.GetAsync($"/api/payments/vnpay/return{ToQueryString(parameters)}");

        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        Assert.Equal(VnPayResponseCodes.Success, JsonDocument.Parse(await ipn.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        var returnResult = (await returned.Content.ReadFromJsonAsync<VnPayReturnResponse>())!;
        Assert.Equal(VnPayResponseCodes.AlreadyProcessed, returnResult.RspCode);
        Assert.Equal(PaymentProcessingStatus.Fulfilled.ToString(), returnResult.Payment!.ProcessingStatus);
    }

    [Fact]
    public async Task Invalid_ipn_signatures_get_the_97_protocol_response()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (_, txnRef, amountMinor, _, _, _) = await CreateBookingWithPaymentAsync(factory);
        var parameters = VnPayCallbacks.SignedCallbackParameters(txnRef, amountMinor);
        parameters["vnp_ResponseCode"] = ["11"];

        using var response = await factory.CreateClient().GetAsync($"/api/payments/vnpay/ipn{ToQueryString(parameters)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VnPayResponseCodes.InvalidSignature,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());
    }

    [Fact]
    public async Task Unknown_ipn_transactions_are_reported_as_not_found()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var parameters = VnPayCallbacks.SignedCallbackParameters("LCUNKNOWN.AAAA", 20000);

        using var response = await factory.CreateClient().GetAsync($"/api/payments/vnpay/ipn{ToQueryString(parameters)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VnPayResponseCodes.TransactionNotFound,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());
    }

    [Fact]
    public async Task Tampered_return_signatures_are_rejected_with_a_problem_response()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (_, txnRef, amountMinor, _, _, _) = await CreateBookingWithPaymentAsync(factory);
        var parameters = VnPayCallbacks.SignedCallbackParameters(txnRef, amountMinor);
        parameters["vnp_Amount"] = [(amountMinor + 100).ToString()];

        using var response = await factory.CreateClient().GetAsync($"/api/payments/vnpay/return{ToQueryString(parameters)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Repeated_query_parameters_are_rejected_as_invalid_signature()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (_, txnRef, amountMinor, _, _, _) = await CreateBookingWithPaymentAsync(factory);
        var parameters = VnPayCallbacks.SignedCallbackParameters(txnRef, amountMinor);
        var query = ToQueryString(parameters) + $"&vnp_Amount={amountMinor}";

        using var response = await factory.CreateClient().GetAsync($"/api/payments/vnpay/ipn{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VnPayResponseCodes.InvalidSignature,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());
    }

    private static string ToQueryString(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters) => "?" + string.Join("&",
        parameters.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value.Single())}"));

    private async Task<(Guid BookingId, string TxnRef, long AmountMinor, Guid ShowtimeId, Guid PaymentId, HttpClient Client)> CreateBookingWithPaymentAsync(
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
        using var bookingResponse = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(Guid.NewGuid(), showtime.Id, seats));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<BookingDetails>())!;
        using var paymentResponse = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var link = (await paymentResponse.Content.ReadFromJsonAsync<BookingPaymentLink>())!;
        var txnRef = Regex.Match(link.PaymentUrl, "vnp_TxnRef=([^&]+)").Groups[1].Value;
        Assert.NotEqual(string.Empty, txnRef);
        return (booking.Id, Uri.UnescapeDataString(txnRef), (long)(booking.TotalAmount * 100), showtime.Id, link.PaymentId, client);
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
            EmailConfirmed = true, IsActive = true, DisplayName = "Callback test"
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
