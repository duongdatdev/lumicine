using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class MovieProjectionRegressionTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Get_and_browse_project_ordered_genres_for_movies()
    {
        await using var db = database.CreateDbContext();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var movie = new Movie(Guid.NewGuid(), $"Projected-{suffix}", 90, now);
        var genres = new[]
        {
            new Genre(Guid.NewGuid(), "Zulu", $"zulu-{Guid.NewGuid():N}", now),
            new Genre(Guid.NewGuid(), "Alpha", $"alpha-{Guid.NewGuid():N}", now)
        };
        db.Movies.Add(movie);
        db.Genres.AddRange(genres);
        db.MovieGenres.AddRange(genres.Select(genre => new MovieGenre(movie.Id, genre.Id, now)));
        await db.SaveChangesAsync();
        var service = DatabaseTestData.CreateCatalogService(db);

        var detail = await service.GetAsync(movie.Id, true, default);
        var page = await service.BrowseAsync(new(), suffix, null, true, default);

        Assert.Equal(["Alpha", "Zulu"], detail!.Genres.Select(genre => genre.Name));
        Assert.Equal(["Alpha", "Zulu"], Assert.Single(page.Items).Genres.Select(genre => genre.Name));
    }
}
