using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;

namespace LumiCine.IntegrationTests.Bookings;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class BookingEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Overlapping_requests_commit_only_the_winners_seats()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (showtime, seats) = await CreateShowtimeAsync();
        using var first = await CreateClientAsync(factory);
        using var second = await CreateClientAsync(factory);

        var requests = await Task.WhenAll(
            first.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(Guid.NewGuid(), showtime, seats[..2])),
            second.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(Guid.NewGuid(), showtime, seats[1..])));
        using var winner = Assert.Single(requests, x => x.StatusCode == HttpStatusCode.Created);
        using var loser = Assert.Single(requests, x => x.StatusCode == HttpStatusCode.Conflict);
        var booking = (await winner.Content.ReadFromJsonAsync<BookingDetails>())!;
        Assert.Equal(2, booking.Seats.Count);
        Assert.Equal(200m, booking.TotalAmount);
        using var guest = factory.CreateClient();
        var snapshot = await guest.GetFromJsonAsync<ShowtimeSeatSummary[]>($"/api/showtimes/{showtime}/seats");
        Assert.Equal(booking.Seats.Select(x => x.SeatId).Order(), snapshot!.Where(x => x.Status == "Held").Select(x => x.SeatId).Order());
        Assert.Single(snapshot!, x => x.Status == "Available");
    }

    [Fact]
    public async Task Notification_failure_keeps_the_committed_hold()
    {
        using var original = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var notifier = new FailingNotifier();
        using var factory = original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IShowtimeAvailabilityNotifier>(notifier))));
        var (showtime, seats) = await CreateShowtimeAsync();
        using var client = await CreateClientAsync(factory);
        var body = new CreateBookingRequest(Guid.NewGuid(), showtime, seats[..1]);

        using var response = await client.PostAsJsonAsync("/api/bookings", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<BookingDetails>())!;
        Assert.Equal(showtime, Assert.Single(notifier.Attempts));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(booking.Id, await db.ShowtimeSeats.Where(x => x.ShowtimeId == showtime && x.SeatId == seats[0])
            .Select(x => x.CurrentBookingId).SingleAsync());
    }

    [Fact]
    public async Task Two_api_instances_expire_an_existing_hold_on_startup()
    {
        using var original = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (showtime, seats) = await CreateShowtimeAsync();
        using var client = await CreateClientAsync(original);
        using var response = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(Guid.NewGuid(), showtime, seats[..1]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<BookingDetails>())!;
        await original.DisposeAsync();

        var clock = new BookingClock(new DateTimeOffset(booking.HoldExpiresAtUtc!.Value));
        using var firstBase = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var secondBase = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var first = firstBase.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        using var second = secondBase.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        await Task.WhenAll(Task.Run(() => _ = first.Services), Task.Run(() => _ = second.Services));

        await using var db = fixture.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await db.Bookings.AnyAsync(x => x.Id == booking.Id && x.Status == BookingStatus.Expired, timeout.Token))
            await Task.Delay(50, timeout.Token);
        var released = await db.ShowtimeSeats.SingleAsync(x => x.ShowtimeId == showtime && x.SeatId == seats[0]);
        Assert.Equal(ShowtimeSeatStatus.Available, released.Status);
        Assert.Null(released.CurrentBookingId);
        Assert.Equal(1, await db.BookingItems.CountAsync(x => x.BookingId == booking.Id));
    }

    [Fact]
    public async Task Same_account_cannot_hold_two_showtimes_and_can_retry_cancel_and_rebook()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var (firstShowtime, firstSeats) = await CreateShowtimeAsync();
        var (secondShowtime, secondSeats) = await CreateShowtimeAsync();
        using var client = await CreateClientAsync(factory);
        using var other = await CreateClientAsync(factory);
        using var guest = factory.CreateClient();
        var bodies = new[]
        {
            new CreateBookingRequest(Guid.NewGuid(), firstShowtime, firstSeats[..2]),
            new CreateBookingRequest(Guid.NewGuid(), secondShowtime, secondSeats[..2])
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/bookings", bodies[0])).StatusCode);

        var responses = await Task.WhenAll(bodies.Select(body => client.PostAsJsonAsync("/api/bookings", body)));
        using var created = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        using var conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var booking = (await created.Content.ReadFromJsonAsync<BookingDetails>())!;
        Assert.Matches("^LC[0-9a-f]{32}$", booking.Code);
        var location = created.Headers.Location!.PathAndQuery;
        var body = Assert.Single(bodies, x => x.ShowtimeId == booking.ShowtimeId);
        using var replay = await client.PostAsJsonAsync("/api/bookings", body with { SeatIds = body.SeatIds.Reverse().ToArray() });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = (await replay.Content.ReadFromJsonAsync<BookingDetails>())!;
        Assert.Equal(booking.Id, replayed.Id);
        Assert.Equal(booking.HoldExpiresAtUtc, replayed.HoldExpiresAtUtc);
        using var changed = await client.PostAsJsonAsync("/api/bookings", body with { SeatIds = body.SeatIds.Take(1).ToArray() });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var problem = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));

        using var foreignRead = await other.GetAsync(location);
        using var foreignCancel = await other.PostAsync(location + "/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignCancel.StatusCode);
        using var ownRead = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.True(ownRead.Headers.CacheControl?.NoStore);

        using var cancelled = await client.PostAsync(location + "/cancel", null);
        using var repeatedCancel = await client.PostAsync(location + "/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedCancel.StatusCode);
        using var cancelledReplay = await client.PostAsJsonAsync("/api/bookings", body);
        Assert.Equal("Cancelled", (await cancelledReplay.Content.ReadFromJsonAsync<BookingDetails>())!.Status);
        using var rebooked = await client.PostAsJsonAsync("/api/bookings", body with { RequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);
        var list = (await client.GetFromJsonAsync<PagedResult<BookingDetails>>("/api/bookings?status=Cancelled"))!;
        Assert.Equal(booking.Id, Assert.Single(list.Items).Id);
        Assert.Equal(2, Assert.Single(list.Items).Seats.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(15)]
    public async Task Hold_expires_at_the_earlier_deadline_and_reclaim_keeps_history(int minutesUntilShowtime)
    {
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var clock = new BookingClock(now);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        var start = now.UtcDateTime.AddMinutes(minutesUntilShowtime);
        var (showtime, seats) = await CreateShowtimeAsync(start);
        using var first = await CreateClientAsync(factory);
        using var second = await CreateClientAsync(factory);
        var body = new CreateBookingRequest(Guid.NewGuid(), showtime, seats[..1]);
        using var created = await first.PostAsJsonAsync("/api/bookings", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = (await created.Content.ReadFromJsonAsync<BookingDetails>())!;
        var deadline = now.AddMinutes(Math.Min(5, minutesUntilShowtime));
        Assert.Equal(deadline.UtcDateTime, booking.HoldExpiresAtUtc);

        clock.Now = deadline.AddTicks(-10);
        Assert.Equal("Pending", (await first.GetFromJsonAsync<BookingDetails>(created.Headers.Location))!.Status);
        clock.Now = deadline;
        Assert.Equal("Expired", (await first.GetFromJsonAsync<BookingDetails>(created.Headers.Location))!.Status);
        var expired = (await first.GetFromJsonAsync<PagedResult<BookingDetails>>("/api/bookings?status=Expired"))!;
        Assert.Equal(booking.Id, Assert.Single(expired.Items).Id);

        using var replacement = await second.PostAsJsonAsync("/api/bookings", body with { RequestId = Guid.NewGuid() });
        if (deadline.UtcDateTime < start)
        {
            Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
            var newBooking = (await replacement.Content.ReadFromJsonAsync<BookingDetails>())!;
            using var cancel = await first.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
            await using var db = fixture.CreateDbContext();
            var held = await db.ShowtimeSeats.SingleAsync(x => x.ShowtimeId == showtime && x.SeatId == seats[0]);
            Assert.Equal(newBooking.Id, held.CurrentBookingId);
            Assert.Equal(1, await db.BookingItems.CountAsync(x => x.BookingId == booking.Id));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, replacement.StatusCode);
        }
    }

    private async Task<(Guid ShowtimeId, Guid[] Seats)> CreateShowtimeAsync(DateTime? start = null)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = start is DateTime startAt
            ? new Showtime(Guid.NewGuid(), movie.Id, room.Id, startAt, startAt.AddHours(2), 100, DateTime.UtcNow)
            : await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        if (start.HasValue)
        {
            db.Showtimes.Add(showtime);
            await db.SaveChangesAsync();
        }
        var seats = new Guid[3];
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
            seats[i] = seat.Id;
        }
        return (showtime.Id, seats);
    }

    private async Task<HttpClient> CreateClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        await using var db = fixture.CreateDbContext();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Booking test"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, ["Customer"]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class BookingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FailingNotifier : IShowtimeAvailabilityNotifier
    {
        public List<Guid> Attempts { get; } = [];

        public Task NotifyAsync(Guid showtimeId, CancellationToken cancellationToken)
        {
            Attempts.Add(showtimeId);
            throw new InvalidOperationException("Injected notification failure.");
        }
    }
}
