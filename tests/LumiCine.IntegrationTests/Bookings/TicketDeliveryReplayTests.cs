using System.Net;
using System.Net.Http.Json;
using LumiCine.Application.Bookings;
using LumiCine.Domain.Bookings;
using Microsoft.Extensions.DependencyInjection;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.IntegrationTests.Payments;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Bookings;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class TicketDeliveryReplayTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Resend_reuses_the_issued_ticket_codes()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (client, booking) = await CreateFulfilledBookingAsync(factory);
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var codesBefore = await TicketCodesAsync(db, booking.Id);

        using var first = await client.PostAsync($"/api/bookings/{booking.Id}/tickets/resend-email", null);
        using var second = await client.PostAsync($"/api/bookings/{booking.Id}/tickets/resend-email", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
        Assert.Equal(codesBefore, await TicketCodesAsync(db, booking.Id));
        var itemIds = await db.BookingItems.Where(x => x.BookingId == booking.Id).Select(x => x.Id).ToListAsync();
        Assert.Equal(itemIds.Count, await db.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId)));
        Assert.Equal(0, await db.Tickets.CountAsync(x => itemIds.Contains(x.BookingItemId) && x.Status == TicketStatus.Used));
    }

    [Fact]
    public async Task Checkin_replays_the_recorded_outcome_even_with_a_different_code()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (staff, ticketCode, ticketId, _) = await SetupConfirmedBookingAsync(factory);
        var requestId = Guid.NewGuid();

        // The first scan misses: an unknown code is recorded as a rejection.
        using var missed = await staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(requestId, "wrong-scanned-code"));
        Assert.Equal(HttpStatusCode.NotFound, missed.StatusCode);

        // The retry replays the recorded rejection instead of scanning the new code.
        using var replay = await staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(requestId, ticketCode));
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(TicketStatus.Issued, (await db.Tickets.SingleAsync(x => x.Id == ticketId)).Status);
        Assert.Equal(1, await db.CheckInLogs.CountAsync(x => x.StaffUserId == staffUserId && x.RequestId == requestId));
    }

    [Fact]
    public async Task Voided_tickets_are_rejected_and_audited()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (staff, ticketCode, ticketId, _) = await SetupConfirmedBookingAsync(factory);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Tickets\" SET \"Status\" = {"Voided"} WHERE \"Id\" = {ticketId}");
        }

        var requestId = Guid.NewGuid();
        using var response = await staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(requestId, ticketCode));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verify = fixture.CreateDbContext();
        var log = await verify.CheckInLogs.SingleAsync(x => x.RequestId == requestId);
        Assert.Equal(CheckInLogResult.TicketVoided, log.Result);
        Assert.Equal(ticketId, log.TicketId);
        // A replay of the same request reports the same outcome.
        using var replay = await staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(requestId, ticketCode));
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    private static async Task<IReadOnlyList<string>> TicketCodesAsync(LumiCineDbContext db, Guid bookingId) =>
        await (from ticket in db.Tickets
               join item in db.BookingItems on ticket.BookingItemId equals item.Id
               where item.BookingId == bookingId
               orderby ticket.Code
               select ticket.Code).ToListAsync();

    private Guid staffUserId;

    private async Task<(HttpClient Staff, string TicketCode, Guid TicketId, Guid ShowtimeId)> SetupConfirmedBookingAsync(
        ApiWebApplicationFactory factory)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        staffUserId = Guid.NewGuid();
        var email = $"{staffUserId:N}@example.com";
        db.Users.Add(new LumiCine.Infrastructure.Identity.ApplicationUser
        {
            Id = staffUserId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Replay staff"
        });
        var customerId = Guid.NewGuid();
        var customerEmail = $"{customerId:N}@example.com";
        db.Users.Add(new LumiCine.Infrastructure.Identity.ApplicationUser
        {
            Id = customerId, Email = customerEmail, NormalizedEmail = customerEmail.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Replay customer"
        });
        var movie = new LumiCine.Domain.Catalog.Movie(Guid.NewGuid(), $"Film-{Guid.NewGuid():N}", 100, now);
        var cinema = new LumiCine.Domain.Cinemas.Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "Cinema", "Address", "City", now);
        var room = new LumiCine.Domain.Cinemas.Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Room", now);
        var start = now.AddMinutes(10);
        var showtime = new LumiCine.Domain.Scheduling.Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddMinutes(90), 100, now);
        var booking = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 100, now.AddMinutes(-2),
            holdExpiresAtUtc: now.AddMinutes(5), userId: customerId, requestId: Guid.NewGuid());
        db.AddRange(movie, cinema, room, showtime, booking);
        booking.Confirm(now);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtimeSeat = new LumiCine.Domain.Scheduling.ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, 100, now);
        await db.ShowtimeSeats.AddAsync(showtimeSeat);
        showtimeSeat.Hold(booking.Id);
        showtimeSeat.Sell(booking.Id);
        var item = new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, now);
        await db.BookingItems.AddAsync(item);
        await db.SaveChangesAsync();
        var ticket = new Ticket(Guid.NewGuid(), item.Id, TicketCode.New(), now, now);
        db.Tickets.Add(ticket);
        var staffSessionId = Guid.NewGuid();
        db.AuthSessions.Add(new LumiCine.Infrastructure.Authentication.AuthSession
        {
            Id = staffSessionId, UserId = staffUserId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1)
        });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(staffUserId, staffSessionId, ["Staff"]).Value;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return (client, ticket.Code, ticket.Id, showtime.Id);
    }

    private async Task<(HttpClient Client, BookingDetails Booking)> CreateFulfilledBookingAsync(ApiWebApplicationFactory factory)
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
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new LumiCine.Infrastructure.Identity.ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Delivery replay"
        });
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new LumiCine.Infrastructure.Authentication.AuthSession
        {
            Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1)
        });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(userId, sessionId, ["Customer"]).Value;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var bookingResponse = await client.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(Guid.NewGuid(), showtime.Id, seats));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<BookingDetails>())!;
        using var paymentResponse = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments/vnpay",
            new LumiCine.Application.Payments.CreateBookingPaymentRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var link = (await paymentResponse.Content.ReadFromJsonAsync<LumiCine.Application.Payments.BookingPaymentLink>())!;
        await using var paymentsDb = fixture.CreateDbContext();
        var payment = await paymentsDb.Payments.SingleAsync(x => x.Id == link.PaymentId);
        var parameters = VnPayCallbacks.SignedCallbackParameters(payment.MerchantReference, (long)(payment.Amount * 100));
        using var scope = factory.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<LumiCine.Application.Payments.IPaymentService>();
        var outcome = await payments.ApplyVnPayCallbackAsync(parameters, CancellationToken.None);
        Assert.Equal(LumiCine.Application.Payments.VnPayResponseCodes.Success, outcome.RspCode);
        return (client, booking);
    }
}
