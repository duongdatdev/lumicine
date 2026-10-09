using LumiCine.Infrastructure;
using LumiCine.Infrastructure.Persistence;
using LumiCine.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DemoSeederTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Demo_seed_is_repeatable_and_keeps_existing_metadata_status_layout_and_cancellations()
    {
        var now = DateTime.UtcNow;
        var customMovieId = DemoId("movie-glass-horizon");
        var customCinemaId = DemoId("cinema-riverlight");
        var customRoomId = DemoId("cinema-riverlight-room-1");
        var customSeatId = DemoId("cinema-riverlight-room-1-seat-X-1");
        await using (var initial = fixture.CreateDbContext())
        {
            initial.Movies.Add(new Movie(customMovieId, "My existing film", 112, now));
            initial.Cinemas.Add(new Cinema(customCinemaId, "CUSTOM-CINEMA", "My existing cinema", "Address", "City", now));
            initial.Rooms.Add(new Room(customRoomId, customCinemaId, "CUSTOM-HALL", "My existing room", now));
            initial.Seats.Add(new Seat(customSeatId, customRoomId, "X", 1, "VIP", now));
            await initial.SaveChangesAsync();
        }

        var settings = new Dictionary<string, string?> { ["ConnectionStrings:LumiCine"] = fixture.ConnectionString };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        await using var provider = services.BuildServiceProvider();
        var seeder = provider.GetRequiredService<DemoSeeder>();

        await seeder.SeedAsync();
        Guid cancelledShowtimeId;
        await using (var db = fixture.CreateDbContext())
        {
            var movie = await db.Movies.SingleAsync(x => x.Id == customMovieId);
            var cinema = await db.Cinemas.SingleAsync(x => x.Id == customCinemaId);
            var room = await db.Rooms.SingleAsync(x => x.Id == customRoomId);
            var seat = await db.Seats.SingleAsync(x => x.Id == customSeatId);
            movie.Update("My edited demo title", movie.OriginalTitle, movie.Synopsis, movie.DurationMinutes, movie.ReleaseDate, movie.AgeRating, DateTime.UtcNow);
            movie.SetActive(false, DateTime.UtcNow);
            cinema.Update("MY-CINEMA", "Edited cinema", "Edited address", "Edited city", DateTime.UtcNow);
            room.Update("EDITED-HALL", "Edited room", DateTime.UtcNow);
            room.SetActive(false, DateTime.UtcNow);
            seat.SetActive(false);
            var localToday = DateOnly.FromDateTime(now.AddHours(7));
            var showtimeId = DemoId($"showtime-cinema-riverlight-room-1-{localToday.AddDays(1):yyyyMMdd}-1400");
            var showtime = await db.Showtimes.SingleAsync(x => x.Id == showtimeId);
            showtime.Cancel(DateTime.UtcNow);
            cancelledShowtimeId = showtime.Id;
            await db.SaveChangesAsync();
        }

        await seeder.SeedAsync();

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(3, await verify.Genres.CountAsync(x => x.Slug.StartsWith("demo-")));
        Assert.Equal(3, await verify.Movies.CountAsync(x => x.Id == DemoId("movie-glass-horizon") || x.Id == DemoId("movie-last-orbit") || x.Id == DemoId("movie-summer-static")));
        Assert.Equal(2, await verify.Cinemas.CountAsync(x => x.Id == DemoId("cinema-riverlight") || x.Id == DemoId("cinema-cloudline")));
        Assert.Equal(4, await verify.Rooms.CountAsync(x => x.Id == DemoId("cinema-riverlight-room-1") || x.Id == DemoId("cinema-riverlight-room-2") || x.Id == DemoId("cinema-cloudline-room-1") || x.Id == DemoId("cinema-cloudline-room-2")));
        Assert.Equal(1, await verify.Seats.CountAsync(x => x.RoomId == customRoomId));
        var showtimeIds = Enumerable.Range(1, 2).SelectMany(day => new[] { "1400", "1700", "2000" }
            .SelectMany(time => new[] { "cinema-riverlight-room-1", "cinema-riverlight-room-2", "cinema-cloudline-room-1", "cinema-cloudline-room-2" }
                .Select(room => DemoId($"showtime-{room}-{DateOnly.FromDateTime(now.AddHours(7)).AddDays(day):yyyyMMdd}-{time}")))).ToArray();
        Assert.Equal(24, await verify.Showtimes.CountAsync(x => showtimeIds.Contains(x.Id)));
        Assert.Contains("My edited demo title", await verify.Movies.Select(x => x.Title).ToArrayAsync());
        Assert.False(await verify.Movies.Where(x => x.Id == customMovieId).Select(x => x.IsActive).SingleAsync());
        Assert.Contains("MY-CINEMA", await verify.Cinemas.Select(x => x.Code).ToArrayAsync());
        Assert.Equal("Edited room", await verify.Rooms.Where(x => x.Id == customRoomId).Select(x => x.Name).SingleAsync());
        Assert.False(await verify.Rooms.Where(x => x.Id == customRoomId).Select(x => x.IsActive).SingleAsync());
        Assert.False(await verify.Seats.Where(x => x.Id == customSeatId).Select(x => x.IsActive).SingleAsync());
        Assert.Equal("Cancelled", (await verify.Showtimes.Where(x => x.Id == cancelledShowtimeId).Select(x => x.Status).SingleAsync()).ToString());
    }

    private static Guid DemoId(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"lumicine.phase3.demo.v1:{name}"))[..16]);
}
