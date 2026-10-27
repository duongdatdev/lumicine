using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class MovieQueriesServiceTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Browsing_filters_case_insensitive_title_genre_and_inactive_movies_with_stable_pages()
    {
        await using var db = database.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var suffix = Guid.NewGuid().ToString("N");
        var term = $"Lumi-{suffix}";
        var genre = await service.CreateGenreAsync($"Drama-{suffix}", $"drama-{suffix}", default);
        var first = await service.CreateAsync(new($"{term} Story", null, null, 90, null, null, [genre.Id]), default);
        var second = await service.CreateAsync(new($"{term} Nights", null, null, 90, null, null, []), default);
        var hidden = await service.CreateAsync(new($"{term} Hidden", null, null, 90, null, null, [genre.Id]), default);
        await service.SetActiveAsync(hidden.Id, false, default);

        var publicResult = await service.BrowseAsync(new(1, 1), term.ToLowerInvariant(), null, false, default);
        Assert.Equal(2, publicResult.TotalCount);
        Assert.Equal(second.Id, Assert.Single(publicResult.Items).Id);
        var nextPage = await service.BrowseAsync(new(2, 1), term, null, false, default);
        Assert.Equal(first.Id, Assert.Single(nextPage.Items).Id);
        var byGenre = await service.BrowseAsync(new(), term, genre.Id, false, default);
        Assert.Equal([first.Id], byGenre.Items.Select(m => m.Id));
        var management = await service.BrowseAsync(new(), term, null, true, default);
        Assert.Equal(3, management.TotalCount);
        Assert.Contains(management.Items, movie => movie.Id == hidden.Id && !movie.IsActive);
    }

}
