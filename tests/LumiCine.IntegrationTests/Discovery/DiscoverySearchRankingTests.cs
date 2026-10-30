using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoverySearchRankingTests(PostgresDatabaseFixture fixture)
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Hybrid_search_fuses_semantic_and_keyword_rankings()
    {
        await using var db = fixture.CreateDbContext();
        var (movieA, movieB, movieC) = await SeedThreeMoviesAsync(db, "qx1",
            ("Dáng đúng", "Tài liệu"), ("Tốc độ", "Hành động"), ("Ký ức", "Tâm lý"));
        await SeedIndexedDocumentAsync(db, movieA.Id, [1f, 0f, 0f, 0f]);
        await SeedIndexedDocumentAsync(db, movieB.Id, [0f, 1f, 0f, 0f]);
        // Movie C intentionally has no indexed document.
        var embedding = new ScriptedEmbeddingGenerator([1f, 0f, 0f, 0f]);
        var service = CreateService(db, embedding);
        // "qx1" narrows candidates to this test's movies; "dáng" lifts A in the keyword ranking.
        var filters = new DiscoveryFilters { TopicText = "dáng", MovieNameText = "qx1" };

        var output = await service.SearchAsync(CreateInput(filters), default);

        Assert.Equal(DiscoveryModes.Hybrid, output.Mode);
        Assert.False(output.IsDegraded);
        Assert.Empty(output.Warnings);
        Assert.Equal(3, output.Movies.Count);
        Assert.Equal([movieA.Id, movieB.Id, movieC.Id], output.Movies.Select(m => m.MovieId).ToList());
        Assert.True(output.Movies[0].Score > output.Movies[1].Score);
        Assert.True(output.Movies[1].Score > output.Movies[2].Score);
        var first = output.Movies[0];
        Assert.StartsWith("Dáng đúng", first.Title);
        Assert.StartsWith("Tài liệu", Assert.Single(first.Genres));
        Assert.Equal("dáng qx1", Assert.Single(embedding.Requests));
    }

    [Fact]
    public async Task Embedding_failure_degrades_to_keyword_ranking_with_warning()
    {
        await using var db = fixture.CreateDbContext();
        var (movieA, movieB, movieC) = await SeedThreeMoviesAsync(db, "qx2",
            ("Dáng đúng", "Tài liệu"), ("Tốc độ", "Hành động"), ("Ký ức", "Tâm lý"));
        var embedding = new ScriptedEmbeddingGenerator([1f, 0f, 0f, 0f]) { Failure = new HttpRequestException("503") };
        var service = CreateService(db, embedding);
        var filters = new DiscoveryFilters { TopicText = "tốc", MovieNameText = "qx2" };

        var output = await service.SearchAsync(CreateInput(filters), default);

        Assert.Equal(DiscoveryModes.Keyword, output.Mode);
        Assert.True(output.IsDegraded);
        Assert.Contains("từ khóa", Assert.Single(output.Warnings));
        // Only "Tốc độ qx2" contains the query term, so it leads with the plain keyword score.
        Assert.Equal(movieB.Id, output.Movies[0].MovieId);
        Assert.Equal(6, output.Movies[0].Score);
        // The remaining two tie at zero matches and fall back to Id order.
        Assert.Equal(new[] { movieA.Id, movieC.Id }.OrderBy(x => x), output.Movies.Skip(1).Select(m => m.MovieId));
        Assert.Equal(output.Movies[1].Score, output.Movies[2].Score);
    }

    [Fact]
    public async Task Keyword_ties_break_toward_the_lower_movie_id()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        // Create the higher id first so candidate order cannot come from insertion order.
        var higher = await SeedMovieAsync(db, Guid.Parse("f0000000-0000-0000-0000-000000000002"), "Phi vụ XYZ qx3", "Hành động");
        var lower = await SeedMovieAsync(db, Guid.Parse("10000000-0000-0000-0000-000000000001"), "Phi vụ XYZ qx3", "Hành động");
        await SeedShowtimeAsync(db, higher.Id, room.Id, offsetMinutes: 0);
        await SeedShowtimeAsync(db, lower.Id, room.Id, offsetMinutes: 150);
        var service = CreateService(db, new ScriptedEmbeddingGenerator([1f, 0f, 0f, 0f]));
        var filters = new DiscoveryFilters { MovieNameText = "qx3" };

        var output = await service.SearchAsync(CreateInput(filters), default);

        Assert.Equal(DiscoveryModes.Keyword, output.Mode);
        Assert.Equal([lower.Id, higher.Id], output.Movies.Select(m => m.MovieId).ToList());
        Assert.Equal(output.Movies[0].Score, output.Movies[1].Score);
    }

    [Fact]
    public async Task Inactive_movies_and_movies_without_window_showtimes_are_excluded()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var active = await SeedMovieAsync(db, "Phim XYZ qx4", "Hành động");
        await SeedShowtimeAsync(db, active.Id, room.Id, offsetMinutes: 0);
        var inactive = await SeedMovieAsync(db, "Phim ẩn qx4", "Hành động");
        await db.Movies.Where(m => m.Id == inactive.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.IsActive, false));
        await SeedShowtimeAsync(db, inactive.Id, room.Id, offsetMinutes: 150);
        var future = await SeedMovieAsync(db, "Phim sau qx4", "Hành động");
        await SeedShowtimeAsync(db, future.Id, room.Id, DateTime.UtcNow.AddDays(20));
        var service = CreateService(db, new ScriptedEmbeddingGenerator([1f, 0f, 0f, 0f]));
        var filters = new DiscoveryFilters { MovieNameText = "qx4" };

        var output = await service.SearchAsync(CreateInput(filters), default);

        Assert.Equal([active.Id], output.Movies.Select(m => m.MovieId).ToList());
    }

    private DiscoverySearchService CreateService(LumiCineDbContext db, ScriptedEmbeddingGenerator embedding) =>
        new(fixture.CreateDbContext(), TimeProvider.System,
            new GeminiAiClients(new ScriptedChatClient(new ScriptedChatReply("ok")), embedding),
            Options.Create(new GeminiOptions { EmbeddingDimensions = 4 }),
            Options.Create(new DiscoveryConversationOptions()));

    private static DiscoverySearchInput CreateInput(DiscoveryFilters filters) =>
        new(Guid.NewGuid(), filters, "gợi ý phim đi", Now);

    private static async Task<(Cinema Cinema, Room Room)> SeedCinemaAsync(LumiCineDbContext db)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "Rạp trung tâm", "12 Nguyễn Huệ", "Hà Nội", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Phòng một", now);
        await db.AddRangeAsync(cinema, room);
        await db.SaveChangesAsync();
        return (cinema, room);
    }

    private static async Task<Movie> SeedMovieAsync(LumiCineDbContext db, Guid movieId, string title, string genreName)
    {
        var now = DateTime.UtcNow;
        var movie = new Movie(movieId, title, 95, now, synopsis: $"Kể về {genreName.ToLowerInvariant()} qua nhiều thập kỷ.");
        // Genre names are unique per database; the hex suffix keeps them distinct without
        // colliding with the qx-marker query tokens used to scope candidates.
        var genre = new Genre(Guid.NewGuid(), $"{genreName} {Guid.NewGuid():N}", Guid.NewGuid().ToString("N"), now);
        var movieGenre = new MovieGenre(movie.Id, genre.Id, now);
        await db.AddRangeAsync(movie, genre, movieGenre);
        await db.SaveChangesAsync();
        return movie;
    }

    private static Task<Movie> SeedMovieAsync(LumiCineDbContext db, string title, string genreName) =>
        SeedMovieAsync(db, Guid.NewGuid(), title, genreName);

    private static async Task<Showtime> SeedShowtimeAsync(LumiCineDbContext db, Guid movieId, Guid roomId, DateTime? startUtc = null, int offsetMinutes = 0)
    {
        var start = startUtc ?? DateTime.UtcNow.AddDays(1).AddMinutes(offsetMinutes);
        var showtime = new Showtime(Guid.NewGuid(), movieId, roomId, start, start.AddMinutes(100), 100_000, DateTime.UtcNow);
        await db.Showtimes.AddAsync(showtime);
        await db.SaveChangesAsync();
        return showtime;
    }

    /// <summary>Seeds three movies sharing the marker so queries can scope to one test.</summary>
    private async Task<(Movie First, Movie Second, Movie Third)> SeedThreeMoviesAsync(
        LumiCineDbContext db, string marker, (string Title, string Genre) first, (string Title, string Genre) second, (string Title, string Genre) third)
    {
        var (_, room) = await SeedCinemaAsync(db);
        var movieA = await SeedMovieAsync(db, $"{first.Title} {marker}", first.Genre);
        var movieB = await SeedMovieAsync(db, $"{second.Title} {marker}", second.Genre);
        var movieC = await SeedMovieAsync(db, $"{third.Title} {marker}", third.Genre);
        await SeedShowtimeAsync(db, movieA.Id, room.Id, offsetMinutes: 0);
        await SeedShowtimeAsync(db, movieB.Id, room.Id, offsetMinutes: 150);
        await SeedShowtimeAsync(db, movieC.Id, room.Id, offsetMinutes: 300);
        return (movieA, movieB, movieC);
    }

    private static async Task SeedIndexedDocumentAsync(LumiCineDbContext db, Guid movieId, float[] vector)
    {
        var document = new MovieSearchDocument(Guid.NewGuid(), movieId, "Văn bản phim", new string('a', 64), DateTime.UtcNow);
        document.MarkIndexed("gemini-embedding-2", vector.Length, vector, DateTime.UtcNow);
        await db.MovieSearchDocuments.AddAsync(document);
        await db.SaveChangesAsync();
    }
}
