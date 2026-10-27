using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class MoviePublicationTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Duration_change_and_deactivation_are_blocked_during_the_showtime_cleanup_window()
    {
        await using var db = database.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var movie = await service.CreateAsync(new("Film", null, null, 90, null, null, []), default);
        var (_, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var now = DateTime.UtcNow;
        db.Showtimes.Add(new Showtime(Guid.NewGuid(), movie.Id, room.Id, now.AddMinutes(-100), now.AddMinutes(-10), 10, now));
        await db.SaveChangesAsync();

        var durationConflict = await Assert.ThrowsAsync<ApiException>(() => service.UpdateAsync(movie.Id,
            new("Film", null, null, 100, null, null, []), default));
        Assert.Equal(409, durationConflict.StatusCode);
        var deactivationConflict = await Assert.ThrowsAsync<ApiException>(() => service.SetActiveAsync(movie.Id, false, default));
        Assert.Equal(409, deactivationConflict.StatusCode);

        Assert.True(await service.SetActiveAsync(movie.Id, true, default));
    }
}
