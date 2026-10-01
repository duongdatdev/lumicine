using LumiCine.Application.Common;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Cinemas;

public sealed partial class CinemaServiceTests
{
    [Fact]
    public async Task Seat_status_change_is_blocked_by_upcoming_showtime()
    {
        await using var db = Fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var service = new CinemaService(db, TimeProvider.System);
        await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);

        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.SetSeatActiveAsync(seat.Id, false, default))).StatusCode);
    }
}
