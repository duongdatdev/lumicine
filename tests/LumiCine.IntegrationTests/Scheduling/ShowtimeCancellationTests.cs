using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace LumiCine.IntegrationTests.Persistence;

public sealed partial class ShowtimeServiceTests
{
[Fact]
    public async Task Held_or_sold_seats_block_changes_and_cancellation_keeps_snapshot()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var firstSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        var secondSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 2);
        var service = new ShowtimeService(db, TimeProvider.System);
        var start = DateTimeOffset.UtcNow.AddDays(14);
        var showtime = await service.CreateAsync(new(movie.Id, room.Id, start.ToString("O"), 100), default);

        await db.ShowtimeSeats.Where(x => x.ShowtimeId == showtime.Id && x.SeatId == firstSeat.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, ShowtimeSeatStatus.Held));
        var heldConflict = await Assert.ThrowsAsync<ApiException>(() => service.RescheduleAsync(showtime.Id,
            new(start.AddDays(1).ToString("O"), 100), default));
        Assert.Equal(409, heldConflict.StatusCode);

        await db.ShowtimeSeats.Where(x => x.ShowtimeId == showtime.Id && x.SeatId == firstSeat.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, ShowtimeSeatStatus.Available));
        await db.ShowtimeSeats.Where(x => x.ShowtimeId == showtime.Id && x.SeatId == secondSeat.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, ShowtimeSeatStatus.Sold));
        var soldConflict = await Assert.ThrowsAsync<ApiException>(() => service.CancelAsync(showtime.Id, default));
        Assert.Equal(409, soldConflict.StatusCode);

        var otherRoom = await DatabaseTestData.CreateRoomAsync(db, cinema.Id);
        var retainedSeat = await DatabaseTestData.CreateSeatAsync(db, otherRoom.Id);
        var cancellable = await service.CreateAsync(new(movie.Id, otherRoom.Id, start.AddDays(2).ToString("O"), 100), default);
        Assert.True(await service.CancelAsync(cancellable.Id, default));
        var retained = await db.ShowtimeSeats.SingleAsync(x => x.ShowtimeId == cancellable.Id && x.SeatId == retainedSeat.Id);
        Assert.Equal(ShowtimeSeatStatus.Available, retained.Status);
        Assert.Equal(ShowtimeStatus.Cancelled, (await db.Showtimes.SingleAsync(x => x.Id == cancellable.Id)).Status);
    }
}
