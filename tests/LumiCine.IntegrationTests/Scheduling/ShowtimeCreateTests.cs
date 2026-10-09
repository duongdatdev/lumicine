using LumiCine.Application.Scheduling;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Persistence;

public sealed partial class ShowtimeServiceTests
{
    [Fact]
    public async Task Create_snapshots_active_seats_at_the_base_price()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 2);
        await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 3, isActive: false);
        var service = new ShowtimeService(db, TimeProvider.System);
        var start = DateTimeOffset.UtcNow.AddDays(4);

        var created = await service.CreateAsync(new(movie.Id, room.Id, start.ToString("O"), 125), default);
        var snapshot = await db.ShowtimeSeats.Where(x => x.ShowtimeId == created.Id).ToListAsync();

        Assert.Equal(2, snapshot.Count);
        Assert.All(snapshot, x =>
        {
            Assert.Equal(ShowtimeSeatStatus.Available, x.Status);
            Assert.Equal(125, x.Price);
        });
    }
}
