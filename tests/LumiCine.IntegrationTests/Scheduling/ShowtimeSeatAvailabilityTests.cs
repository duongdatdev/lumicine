using LumiCine.Application.Scheduling;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using LumiCine.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Persistence;

public sealed partial class ShowtimeServiceTests
{
    [Fact]
    public async Task Public_and_management_seat_lists_return_snapshot_state_and_public_hides_inactive_seats()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var active = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 2, isActive: false);
        var service = new ShowtimeService(db, TimeProvider.System);
        var showtime = await service.CreateAsync(new(movie.Id, room.Id, DateTimeOffset.UtcNow.AddDays(18).ToString("O"), 90), default);

        var publicSeats = await service.GetSeatsAsync(showtime.Id, false, default);
        var managementSeats = await service.GetSeatsAsync(showtime.Id, true, default);
        var publicSeat = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ShowtimeSeatSummary>>(publicSeats));
        var managementSeat = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ShowtimeSeatSummary>>(managementSeats));
        Assert.Equal(active.Id, publicSeat.SeatId);
        Assert.Equal(active.Id, managementSeat.SeatId);

        await db.ShowtimeSeats.Where(x => x.ShowtimeId == showtime.Id && x.SeatId == active.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, ShowtimeSeatStatus.Held));
        var updatedSeats = await service.GetSeatsAsync(showtime.Id, false, default);
        Assert.Equal("Held", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ShowtimeSeatSummary>>(updatedSeats)).Status);
    }

    [Fact]
    public async Task Public_seat_snapshot_treats_an_expired_owned_hold_as_available()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        var now = DateTime.UtcNow;
        var showtime = await new ShowtimeService(db, TimeProvider.System).CreateAsync(
            new(movie.Id, room.Id, DateTimeOffset.UtcNow.AddDays(18).ToString("O"), 90), default);
        var owner = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 90,
            now.AddMinutes(-6), holdExpiresAtUtc: now.AddMinutes(-1));
        db.Bookings.Add(owner);
        var showtimeSeat = await db.ShowtimeSeats.SingleAsync(x => x.ShowtimeId == showtime.Id && x.SeatId == seat.Id);
        showtimeSeat.Hold(owner.Id);
        await db.SaveChangesAsync();

        var service = new ShowtimeService(db, new SeatAvailabilityFixedClock(new DateTimeOffset(now, TimeSpan.Zero)));
        var publicSeat = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ShowtimeSeatSummary>>(
            await service.GetSeatsAsync(showtime.Id, false, default)));
        var managementSeat = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ShowtimeSeatSummary>>(
            await service.GetSeatsAsync(showtime.Id, true, default)));

        Assert.Equal("Available", publicSeat.Status);
        Assert.Equal("Held", managementSeat.Status);
    }

    private sealed class SeatAvailabilityFixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
