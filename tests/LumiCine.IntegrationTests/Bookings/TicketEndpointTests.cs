using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Payments;
using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.IntegrationTests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Bookings;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class TicketEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Fulfilled_bookings_expose_their_tickets_to_the_owner_only()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        var (foreign, _) = await CreateClientAsync(factory);

        using var list = await client.GetAsync($"/api/bookings/{booking.Id}/tickets");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        var tickets = (await list.Content.ReadFromJsonAsync<IReadOnlyList<TicketDetails>>())!;
        Assert.Equal(2, tickets.Count);
        Assert.All(tickets, ticket => Assert.Equal(booking.Id, ticket.BookingId));
        Assert.All(tickets, ticket => Assert.Equal(TicketStatus.Issued.ToString(), ticket.Status));
        Assert.All(tickets, ticket => Assert.Matches("^[A-Za-z0-9_-]{43}$", ticket.TicketCode));
        Assert.All(tickets, ticket => Assert.Equal(100, ticket.UnitPrice));
        Assert.All(tickets, ticket => Assert.Equal(booking.MovieTitle, ticket.MovieTitle));
        Assert.All(tickets, ticket => Assert.Equal(booking.ShowtimeId, ticket.ShowtimeId));
        Assert.Equal(tickets.Select(x => x.TicketCode).ToHashSet().Count, tickets.Count);
        Assert.Equal(tickets.OrderBy(x => x.Number).Select(x => x.Number), tickets.Select(x => x.Number));

        using var single = await client.GetAsync($"/api/tickets/{tickets[0].Id}");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        var details = (await single.Content.ReadFromJsonAsync<TicketDetails>())!;
        Assert.Equal(tickets[0].TicketCode, details.TicketCode);

        using var foreignList = await foreign.GetAsync($"/api/bookings/{booking.Id}/tickets");
        using var foreignTicket = await foreign.GetAsync($"/api/tickets/{tickets[0].Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTicket.StatusCode);

        using var anonymous = await factory.CreateClient().GetAsync($"/api/tickets/{tickets[0].Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Pending_bookings_have_no_tickets_yet()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreatePendingBookingAsync(factory);

        using var list = await client.GetAsync($"/api/bookings/{booking.Id}/tickets");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Empty((await list.Content.ReadFromJsonAsync<IReadOnlyList<TicketDetails>>())!);
    }

    [Fact]
    public async Task Resend_confirmation_email_queues_a_fresh_delivery_once_per_minute()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        await using (var db = fixture.CreateDbContext())
        {
            await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        }

        using var first = await client.PostAsync($"/api/bookings/{booking.Id}/tickets/resend-email", null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        await using (var verify = fixture.CreateDbContext())
        {
            var pending = await verify.TicketEmailDeliveries.AsNoTracking()
                .SingleAsync(x => x.BookingId == booking.Id && x.Status == TicketEmailDeliveryStatus.Pending);
            Assert.Equal(0, pending.AttemptCount);
            // The requeued delivery reuses the already issued tickets.
            var itemIds = await verify.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
            Assert.Equal(itemIds.Count, await verify.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
        }

        using var second = await client.PostAsync($"/api/bookings/{booking.Id}/tickets/resend-email", null);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
    }

    [Fact]
    public async Task Resend_confirmation_email_rejects_foreign_and_unissued_bookings()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        var (foreign, _) = await CreateClientAsync(factory);
        var (pendingClient, pendingBooking) = await CreatePendingBookingAsync(factory);

        using var foreignResend = await foreign.PostAsync($"/api/bookings/{booking.Id}/tickets/resend-email", null);
        using var unissued = await pendingClient.PostAsync($"/api/bookings/{pendingBooking.Id}/tickets/resend-email", null);
        using var anonymous = await factory.CreateClient().PostAsync($"/api/bookings/{booking.Id}/tickets/resend-email", null);

        Assert.Equal(HttpStatusCode.NotFound, foreignResend.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unissued.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Used_tickets_expose_the_used_time_to_the_owner()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        await using (var db = fixture.CreateDbContext())
        {
            var itemIds = await db.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
            var ticket = await db.Tickets.FirstAsync(x => itemIds.Contains(x.BookingItemId));
            ticket.MarkUsed(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var list = await client.GetAsync($"/api/bookings/{booking.Id}/tickets");

        var tickets = (await list.Content.ReadFromJsonAsync<IReadOnlyList<TicketDetails>>())!;
        Assert.Single(tickets, ticket => ticket.Status == TicketStatus.Used.ToString());
        Assert.All(tickets.Where(x => x.Status == TicketStatus.Used.ToString()), ticket => Assert.NotNull(ticket.UsedAtUtc));
    }

    private async Task<(HttpClient Client, LumiCine.Application.Bookings.BookingDetails Booking)> CreatePendingBookingAsync(
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
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<LumiCine.Application.Bookings.BookingDetails>())!;
        return (client, booking);
    }

    private async Task<(HttpClient Client, LumiCine.Application.Bookings.BookingDetails Booking)> CreateFulfilledBookingAsync(
        ApiWebApplicationFactory factory)
    {
        var (client, booking) = await CreatePendingBookingAsync(factory);
        using var paymentResponse = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var link = (await paymentResponse.Content.ReadFromJsonAsync<BookingPaymentLink>())!;
        var txnRef = Regex.Match(link.PaymentUrl, "vnp_TxnRef=([^&]+)").Groups[1].Value;
        await using var db = fixture.CreateDbContext();
        var payment = await db.Payments.SingleAsync(x => x.Id == link.PaymentId);
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment.MerchantReference, (long)(payment.Amount * 100));
        using var scope = factory.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        var outcome = await payments.ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
        Assert.Equal(VnPayResponseCodes.Success, outcome.RspCode);
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
            EmailConfirmed = true, IsActive = true, DisplayName = "Ticket test"
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
