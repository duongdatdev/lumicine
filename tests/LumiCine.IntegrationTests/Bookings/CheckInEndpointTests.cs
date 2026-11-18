using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Bookings;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class CheckInEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Staff_checks_in_a_ticket_inside_the_window()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var setup = await SetupConfirmedBookingAsync(factory, startOffset: TimeSpan.FromMinutes(10));
        var requestId = Guid.NewGuid();

        using var response = await setup.Staff.PostAsJsonAsync("/api/staff/check-ins", new CheckInRequest(requestId, setup.TicketCode));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<CheckInResult>())!;
        Assert.Equal(CheckInOutcomes.Success, result.Outcome);
        Assert.Equal(setup.TicketId, result.TicketId);
        Assert.Equal(setup.ShowtimeId, result.ShowtimeId);
        Assert.NotNull(result.UsedAtUtc);
        Assert.NotNull(result.WindowStartsAtUtc);
        Assert.NotNull(result.Ticket);
        Assert.False(string.IsNullOrWhiteSpace(result.Ticket.MovieTitle));
        Assert.False(string.IsNullOrWhiteSpace(result.Ticket.RoomName));
        Assert.True(result.Ticket.Number > 0);
        Assert.Equal(requestId, result.RequestId);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(TicketStatus.Used, (await db.Tickets.SingleAsync(x => x.Id == setup.TicketId)).Status);
        var log = await db.CheckInLogs.SingleAsync(x => x.RequestId == requestId);
        Assert.Equal(CheckInLogResult.Success, log.Result);
        Assert.Equal(setup.TicketId, log.TicketId);
    }

    [Fact]
    public async Task Scans_outside_the_window_are_rejected_and_audited()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var early = await SetupConfirmedBookingAsync(factory, startOffset: TimeSpan.FromMinutes(31));
        using var earlyResponse = await early.Staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(Guid.NewGuid(), early.TicketCode));
        Assert.Equal(HttpStatusCode.Conflict, earlyResponse.StatusCode);

        var late = await SetupConfirmedBookingAsync(factory, startOffset: TimeSpan.FromMinutes(-91));
        using var lateResponse = await late.Staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(Guid.NewGuid(), late.TicketCode));
        Assert.Equal(HttpStatusCode.Conflict, lateResponse.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.CheckInLogs.CountAsync(x => x.AttemptedTicketCode == early.TicketCode && x.Result == CheckInLogResult.OutsideWindow));
        Assert.Equal(1, await db.CheckInLogs.CountAsync(x => x.AttemptedTicketCode == late.TicketCode && x.Result == CheckInLogResult.OutsideWindow));
        Assert.Equal(TicketStatus.Issued, (await db.Tickets.SingleAsync(x => x.Id == early.TicketId)).Status);
        Assert.Equal(TicketStatus.Issued, (await db.Tickets.SingleAsync(x => x.Id == late.TicketId)).Status);
    }

    [Fact]
    public async Task Unknown_codes_are_audited_and_reported_as_not_found()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var setup = await SetupConfirmedBookingAsync(factory);
        var requestId = Guid.NewGuid();

        using var response = await setup.Staff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(requestId, "not-a-real-ticket-code"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        var log = await db.CheckInLogs.SingleAsync(x => x.RequestId == requestId);
        Assert.Equal(CheckInLogResult.TicketNotFound, log.Result);
        Assert.Equal("not-a-real-ticket-code", log.AttemptedTicketCode);
        Assert.Null(log.TicketId);
    }

    [Fact]
    public async Task Replayed_requests_return_the_recorded_outcome()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var setup = await SetupConfirmedBookingAsync(factory, startOffset: TimeSpan.FromMinutes(10));
        var requestId = Guid.NewGuid();
        using var first = await setup.Staff.PostAsJsonAsync("/api/staff/check-ins", new CheckInRequest(requestId, setup.TicketCode));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await setup.Staff.PostAsJsonAsync("/api/staff/check-ins", new CheckInRequest(requestId, setup.TicketCode));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var result = (await replay.Content.ReadFromJsonAsync<CheckInResult>())!;
        Assert.Equal(CheckInOutcomes.Success, result.Outcome);
        Assert.Equal(setup.TicketId, result.TicketId);

        // A different staff member scanning the used ticket gets a duplicate rejection instead.
        var (otherStaff, _) = await CreateStaffClientAsync(factory, ["Staff"]);
        using var duplicate = await otherStaff.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(Guid.NewGuid(), setup.TicketCode));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var duplicateResult = (await duplicate.Content.ReadFromJsonAsync<CheckInResult>())!;
        Assert.Equal(CheckInOutcomes.DuplicateScan, duplicateResult.Outcome);
        Assert.Equal(result.Ticket, duplicateResult.Ticket);
        Assert.NotNull(duplicateResult.UsedAtUtc);
    }

    [Fact]
    public async Task Concurrent_scans_of_one_ticket_produce_a_single_success()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var setup = await SetupConfirmedBookingAsync(factory, startOffset: TimeSpan.FromMinutes(10));
        var (otherStaff, _) = await CreateStaffClientAsync(factory, ["Staff"]);
        var responses = await Task.WhenAll(
            setup.Staff.PostAsJsonAsync("/api/staff/check-ins", new CheckInRequest(Guid.NewGuid(), setup.TicketCode)),
            otherStaff.PostAsJsonAsync("/api/staff/check-ins", new CheckInRequest(Guid.NewGuid(), setup.TicketCode)));

        var outcomes = responses.Select(response =>
            response.StatusCode == HttpStatusCode.OK
                ? CheckInOutcomes.Success
                : response.StatusCode == HttpStatusCode.Conflict ? CheckInOutcomes.DuplicateScan : "other").ToArray();
        Assert.Single(outcomes, outcome => outcome == CheckInOutcomes.Success);
        Assert.Single(outcomes, outcome => outcome == CheckInOutcomes.DuplicateScan);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.CheckInLogs.CountAsync(x => x.AttemptedTicketCode == setup.TicketCode));
        Assert.Equal(1, await db.CheckInLogs.CountAsync(x => x.Result == CheckInLogResult.Success && x.AttemptedTicketCode == setup.TicketCode));
    }

    [Fact]
    public async Task Only_staff_and_admins_may_check_in()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var setup = await SetupConfirmedBookingAsync(factory, startOffset: TimeSpan.FromMinutes(10));
        var (customer, _) = await CreateStaffClientAsync(factory, ["Customer"]);

        using var forbidden = await customer.PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(Guid.NewGuid(), setup.TicketCode));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var unauthorized = await factory.CreateClient().PostAsJsonAsync("/api/staff/check-ins",
            new CheckInRequest(Guid.NewGuid(), setup.TicketCode));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private async Task<(HttpClient Staff, string TicketCode, Guid TicketId, Guid ShowtimeId)> SetupConfirmedBookingAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, TimeSpan startOffset = default)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Check-in booking"
        });
        var movie = new Movie(Guid.NewGuid(), $"Film-{Guid.NewGuid():N}", 100, now);
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "Cinema", "Address", "City", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Room", now);
        var start = now.Add(startOffset == default ? TimeSpan.FromMinutes(10) : startOffset);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddMinutes(90), 100, now);
        var booking = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 100, now.AddMinutes(-2),
            holdExpiresAtUtc: now.AddMinutes(5), userId: userId, requestId: Guid.NewGuid());
        db.AddRange(movie, cinema, room, showtime, booking);
        booking.Confirm(now);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtimeSeat = new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, 100, now);
        await db.ShowtimeSeats.AddAsync(showtimeSeat);
        showtimeSeat.Hold(booking.Id);
        showtimeSeat.Sell(booking.Id);
        var item = new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, now);
        await db.BookingItems.AddAsync(item);
        await db.SaveChangesAsync();
        var ticket = new Ticket(Guid.NewGuid(), item.Id, TicketCode.New(), now, now);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var (staff, _) = await CreateStaffClientAsync(factory, ["Staff"]);
        return (staff, ticket.Code, ticket.Id, showtime.Id);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateStaffClientAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string[] roles)
    {
        await using var db = fixture.CreateDbContext();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Check-in staff"
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
