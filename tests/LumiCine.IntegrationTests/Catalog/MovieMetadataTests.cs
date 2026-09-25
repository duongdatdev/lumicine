using LumiCine.Application.Catalog;
using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class MovieMetadataTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Create_and_update_trim_metadata_and_metadata_edits_preserve_the_poster_url()
    {
        await using var db = database.CreateDbContext();
        var service = new MovieCatalogService(db, TimeProvider.System);
        var suffix = Guid.NewGuid().ToString("N");
        var movie = await service.CreateAsync(new($" Movie-{suffix} ", $" Original-{suffix} ", " Synopsis ", 90,
            new DateOnly(2026, 10, 1), " PG-13 ", []), default);

        Assert.Equal($"Movie-{suffix}", movie.Title);
        Assert.Equal($"Original-{suffix}", movie.OriginalTitle);
        Assert.Equal("Synopsis", movie.Synopsis);
        Assert.Equal("PG-13", movie.AgeRating);

        var trackedMovie = await db.Movies.FindAsync(movie.Id);
        var posterUrl = $"/api/media/posters/{Guid.NewGuid():D}";
        trackedMovie!.SetPosterUrl(posterUrl, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var updated = await service.UpdateAsync(movie.Id,
            new($" Revised-{suffix} ", null, " Revised synopsis ", 90, null, null, []), default);

        Assert.Equal($"Revised-{suffix}", updated!.Title);
        Assert.Equal("Revised synopsis", updated.Synopsis);
        Assert.Equal(posterUrl, updated.PosterUrl);
    }
}
