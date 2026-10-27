using LumiCine.Infrastructure.Catalog;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class GenreQueriesServiceTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Public_and_management_genre_lists_are_stably_paginated()
    {
        await using var db = database.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var suffix = Guid.NewGuid().ToString("N");
        var firstId = (await service.CreateGenreAsync($"ZZZ-A-Genre-{suffix}", $"zzz-a-genre-{suffix}", default)).Id;
        var secondId = (await service.CreateGenreAsync($"ZZZ-B-Genre-{suffix}", $"zzz-b-genre-{suffix}", default)).Id;

        var all = await service.GetGenresAsync(new(1, 100), default);
        var firstIndex = all.Items.ToList().FindIndex(g => g.Id == firstId);
        var secondIndex = all.Items.ToList().FindIndex(g => g.Id == secondId);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex);
        Assert.Equal(firstId, Assert.Single((await service.GetGenresAsync(new(firstIndex + 1, 1), default)).Items).Id);
        Assert.Equal(secondId, Assert.Single((await service.GetGenresAsync(new(secondIndex + 1, 1), default)).Items).Id);
        Assert.Equal(all.TotalCount, await db.Genres.CountAsync());
    }
}
