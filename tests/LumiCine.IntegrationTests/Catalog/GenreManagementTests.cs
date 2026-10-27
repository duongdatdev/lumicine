using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class GenreManagementTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Genre_metadata_is_trimmed_slug_is_lowercase_and_missing_updates_return_null()
    {
        await using var db = database.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var suffix = Guid.NewGuid().ToString("N");
        var genre = await service.CreateGenreAsync($" Drama-{suffix} ", $"DRAMA-{suffix}", default);

        var updated = await service.UpdateGenreAsync(genre.Id, $" Sci-Fi-{suffix} ", $" Sci-Fi-{suffix} ", default);

        Assert.Equal($"Sci-Fi-{suffix}", updated!.Name);
        Assert.Equal($"sci-fi-{suffix}", updated.Slug);
        Assert.Null(await service.UpdateGenreAsync(Guid.NewGuid(), "Missing", "missing", default));
    }

    [Fact]
    public async Task Duplicate_genre_names_or_slugs_return_conflict()
    {
        var name = $"Drama-{Guid.NewGuid():N}";
        var slug = $"drama-{Guid.NewGuid():N}";
        await using (var db = database.CreateDbContext())
            await DatabaseTestData.CreateCatalogService(db).CreateGenreAsync(name, slug, default);

        await using (var db = database.CreateDbContext())
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() =>
                DatabaseTestData.CreateCatalogService(db).CreateGenreAsync(name, $"other-{Guid.NewGuid():N}", default))).StatusCode);

        await using (var db = database.CreateDbContext())
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() =>
                DatabaseTestData.CreateCatalogService(db).CreateGenreAsync($"other-{Guid.NewGuid():N}", slug, default))).StatusCode);
    }

    [Fact]
    public async Task Assigned_genre_cannot_be_deleted_and_unused_genre_can_be_deleted()
    {
        await using var db = database.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var assignedGenre = await service.CreateGenreAsync($"Genre-{Guid.NewGuid():N}", $"genre-{Guid.NewGuid():N}", default);
        var movie = new Movie(Guid.NewGuid(), $"Movie-{Guid.NewGuid():N}", 90, DateTime.UtcNow);
        db.Movies.Add(movie);
        db.MovieGenres.Add(new MovieGenre(movie.Id, assignedGenre.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();

        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.DeleteGenreAsync(assignedGenre.Id, default))).StatusCode);
        var unusedGenre = await service.CreateGenreAsync($"Unused-{Guid.NewGuid():N}", $"unused-{Guid.NewGuid():N}", default);
        Assert.True(await service.DeleteGenreAsync(unusedGenre.Id, default));
        Assert.False(await service.DeleteGenreAsync(Guid.NewGuid(), default));
    }
}
