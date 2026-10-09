using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LumiCine.Infrastructure.Seeding;

public sealed class DemoSeeder(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<DemoSeeder> logger)
{
    private const string Namespace = "lumicine.phase3.demo.v1";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedCatalogAndCinemasAsync(cancellationToken);
        await SeedShowtimesAsync(cancellationToken);
    }

    private async Task SeedCatalogAndCinemasAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var genreData = new[]
        {
            (Key: "genre-action", Name: "Demo Adventure", Slug: "demo-adventure"),
            (Key: "genre-drama", Name: "Demo Drama", Slug: "demo-drama"),
            (Key: "genre-scifi", Name: "Demo Science Fiction", Slug: "demo-science-fiction")
        };
        var genreIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var genreInfo in genreData)
        {
            var id = Id(genreInfo.Key);
            var existing = await db.Genres.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? await db.Genres.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == genreInfo.Slug, ct);
            if (existing is null)
                db.Genres.Add(new Genre(id, genreInfo.Name, genreInfo.Slug, now));
            genreIds.Add(genreInfo.Key, existing?.Id ?? id);
        }

        var films = new[]
        {
            (Key: "movie-glass-horizon", Title: "The Glass Horizon", Original: "Kính Chân Trời", Synopsis: "A coastal engineer finds a signal beneath a flooded city.", Duration: 112, Rating: "T13", Genres: new[] { "genre-drama", "genre-scifi" }),
            (Key: "movie-last-orbit", Title: "The Last Orbit", Original: "Quỹ Đạo Cuối", Synopsis: "A rescue crew races to bring a stranded station home.", Duration: 105, Rating: "T13", Genres: new[] { "genre-action", "genre-scifi" }),
            (Key: "movie-summer-static", Title: "Summer Static", Original: "Mùa Hè Nhiễu Sóng", Synopsis: "Two siblings restore an old radio and uncover a family story.", Duration: 98, Rating: "P", Genres: new[] { "genre-drama" })
        };
        foreach (var film in films)
        {
            var movieId = Id(film.Key);
            if (!await db.Movies.AnyAsync(x => x.Id == movieId, ct))
            {
                db.Movies.Add(new Movie(movieId, film.Title, film.Duration, now, film.Original, film.Synopsis,
                    DateOnly.FromDateTime(now), film.Rating));
                foreach (var genreKey in film.Genres)
                    db.MovieGenres.Add(new MovieGenre(movieId, genreIds[genreKey], now));
            }
        }

        var cinemas = new[]
        {
            (Key: "cinema-riverlight", Code: "RIVERLIGHT", Name: "Riverlight Cinema", Address: "18 Riverside Avenue", City: "Ho Chi Minh City"),
            (Key: "cinema-cloudline", Code: "CLOUDLINE", Name: "Cloudline Cinema", Address: "42 Garden Street", City: "Ha Noi")
        };
        for (var cinemaIndex = 0; cinemaIndex < cinemas.Length; cinemaIndex++)
        {
            var data = cinemas[cinemaIndex];
            var cinemaId = Id(data.Key);
            if (!await db.Cinemas.AnyAsync(x => x.Id == cinemaId, ct))
                db.Cinemas.Add(new Cinema(cinemaId, data.Code, data.Name, data.Address, data.City, now));
            for (var roomIndex = 1; roomIndex <= 2; roomIndex++)
            {
                var roomKey = $"{data.Key}-room-{roomIndex}";
                var roomId = Id(roomKey);
                var newRoom = !await db.Rooms.AnyAsync(x => x.Id == roomId, ct);
                if (newRoom)
                    db.Rooms.Add(new Room(roomId, cinemaId, $"HALL{roomIndex}", $"Auditorium {roomIndex}", now));
                if (newRoom)
                {
                    for (var row = 0; row < 5; row++)
                    {
                        var rowLabel = ((char)('A' + row)).ToString();
                        for (var number = 1; number <= 10; number++)
                        {
                            var seatType = number <= 8 ? "Standard" : "VIP";
                            var seatId = Id($"{roomKey}-seat-{rowLabel}-{number}");
                            db.Seats.Add(new Seat(seatId, roomId, rowLabel, number, seatType, now));
                        }
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedShowtimesAsync(CancellationToken ct)
    {
        var localToday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime.AddHours(7));
        var times = new[] { new TimeOnly(14, 0), new TimeOnly(17, 0), new TimeOnly(20, 0) };
        var roomKeys = new[]
        {
            "cinema-riverlight-room-1", "cinema-riverlight-room-2",
            "cinema-cloudline-room-1", "cinema-cloudline-room-2"
        };
        var movieKeys = new[] { "movie-glass-horizon", "movie-last-orbit", "movie-summer-static" };
        for (var dayOffset = 1; dayOffset <= 2; dayOffset++)
        for (var roomIndex = 0; roomIndex < roomKeys.Length; roomIndex++)
        for (var slotIndex = 0; slotIndex < times.Length; slotIndex++)
        {
            var date = localToday.AddDays(dayOffset);
            var localStart = date.ToDateTime(times[slotIndex], DateTimeKind.Unspecified);
            var offsetStart = new DateTimeOffset(localStart, TimeSpan.FromHours(7));
            var roomKey = roomKeys[roomIndex];
            var key = $"showtime-{roomKey}-{date:yyyyMMdd}-{times[slotIndex]:HHmm}";
            var id = Id(key);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
            if (await db.Showtimes.AnyAsync(x => x.Id == id, ct)) continue;
            var service = scope.ServiceProvider.GetRequiredService<IShowtimeService>();
            try
            {
                await service.CreateWithIdAsync(id, new ShowtimeRequest(
                    Id(movieKeys[(dayOffset + roomIndex + slotIndex - 1) % movieKeys.Length]),
                    Id(roomKey),
                    offsetStart.ToString("O", CultureInfo.InvariantCulture), 120_000m), ct);
            }
            catch (ApiException exception) when (exception.StatusCode == 409)
            {
                logger.LogWarning("Skipped demo showtime {ShowtimeId} because its room is unavailable: {Reason}", id, exception.Message);
            }
        }
    }

    private static Guid Id(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{Namespace}:{name}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
