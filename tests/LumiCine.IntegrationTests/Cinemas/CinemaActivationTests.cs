using LumiCine.Application.Common;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Cinemas;

public sealed partial class CinemaServiceTests
{
    [Fact]
    public async Task Cinema_and_room_deactivation_are_blocked_by_upcoming_showtimes()
    {
        await using var db = Fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var service = new CinemaService(db, TimeProvider.System);
        await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);

        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.SetCinemaActiveAsync(cinema.Id, false, default))).StatusCode);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.SetRoomActiveAsync(room.Id, false, default))).StatusCode);
    }
}
