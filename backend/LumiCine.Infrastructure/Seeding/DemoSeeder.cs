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

    private static Guid Id(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{Namespace}:{name}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
