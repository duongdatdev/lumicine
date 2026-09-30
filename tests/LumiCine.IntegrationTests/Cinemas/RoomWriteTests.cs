using LumiCine.Application.Cinemas;
using LumiCine.Application.Common;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Cinemas;

public sealed partial class CinemaServiceTests
{
    [Fact]
    public async Task Room_codes_are_normalized_updates_succeed_and_duplicates_conflict()
    {
        await using var db = Fixture.CreateDbContext();
        var (movie, cinema, _) = await DatabaseTestData.CreateBaseAsync(db);
        var service = new CinemaService(db, TimeProvider.System);
        var firstId = await service.CreateRoomAsync(cinema.Id, new(" r-1 ", " First "), default);
        var room = await service.GetAdminRoomAsync(firstId, default);
        Assert.Equal("R-1", room!.Code);
        Assert.Equal("First", room.Name);

        Assert.True(await service.UpdateRoomAsync(firstId, new(" r-2 ", " Updated "), default));
        room = await service.GetAdminRoomAsync(firstId, default);
        Assert.Equal("R-2", room!.Code);
        Assert.Equal("Updated", room.Name);
        await service.CreateRoomAsync(cinema.Id, new("R-3", "Third"), default);
        var conflict = await Assert.ThrowsAsync<ApiException>(() => service.UpdateRoomAsync(firstId, new(" r-3 ", "Duplicate "), default));
        Assert.Equal(409, conflict.StatusCode);
    }
}
