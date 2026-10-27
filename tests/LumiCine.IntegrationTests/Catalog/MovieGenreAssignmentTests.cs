using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class MovieGenreAssignmentTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Genre_assignment_rejects_duplicate_and_missing_ids_without_partial_changes_and_keeps_existing_links()
    {
        await using var db = database.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var genre = await service.CreateGenreAsync($"Drama-{Guid.NewGuid():N}", $"drama-{Guid.NewGuid():N}", default);
        var movie = await service.CreateAsync(new("Film", null, null, 90, null, null, []), default);

        await Assert.ThrowsAsync<ApiException>(() => service.AssignGenresAsync(movie.Id, [genre.Id, genre.Id], default));
        await Assert.ThrowsAsync<ApiException>(() => service.AssignGenresAsync(movie.Id, [genre.Id, Guid.NewGuid()], default));
        Assert.Empty((await service.GetAsync(movie.Id, true, default))!.Genres);

        await service.AssignGenresAsync(movie.Id, [genre.Id], default);
        await service.UpdateAsync(movie.Id, new("Film edited", null, null, 90, null, null, [genre.Id]), default);
        Assert.Single((await service.GetAsync(movie.Id, true, default))!.Genres);
    }
}
