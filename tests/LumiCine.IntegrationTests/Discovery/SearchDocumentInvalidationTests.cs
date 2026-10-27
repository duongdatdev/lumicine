using LumiCine.Application.Catalog;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class SearchDocumentInvalidationTests(PostgresDatabaseFixture fixture)
{
    private static MovieSearchDocumentBuilder CreateBuilder() =>
        new(Options.Create(new GeminiOptions()));

    [Fact]
    public async Task Creating_a_movie_queues_one_pending_document_with_the_builder_hash()
    {
        await using var db = fixture.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var genres = await SeedGenresAsync(service);
        var title = $"Dáng đúng-{Guid.NewGuid():N}";
        var synopsis = "Một phim gia đình nhẹ nhàng.";
        var genreNames = genres.Select(g => g.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();

        var movie = await service.CreateAsync(new MovieMetadata(title, null, synopsis, 90, null, null, genres.Select(g => g.Id).ToList()), default);

        var builder = CreateBuilder();
        var expectedText = builder.BuildDocumentText(title, null, synopsis, genreNames);
        var document = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Equal(1, await db.MovieSearchDocuments.CountAsync(d => d.MovieId == movie.Id));
        Assert.Equal(builder.ComputeSourceHash(expectedText), document.SourceHash);
        Assert.Equal(expectedText, document.DocumentText);
        Assert.Contains(title, document.DocumentText);
        Assert.Contains(genreNames[0], document.DocumentText);
        Assert.Contains(genreNames[1], document.DocumentText);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.Equal(0, document.AttemptCount);
        Assert.Null(document.Vector);
    }

    [Fact]
    public async Task Updating_the_movie_title_requeues_the_same_row_with_a_new_hash()
    {
        await using var db = fixture.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var genres = await SeedGenresAsync(service);
        var movie = await service.CreateAsync(new MovieMetadata($"Cô gái đến từ hôm qua-{Guid.NewGuid():N}", null, null, 100, null, null, genres.Select(g => g.Id).ToList()), default);
        var original = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);

        var newTitle = $"Cô gái đến từ hôm qua (lồng tiếng)-{Guid.NewGuid():N}";
        await service.UpdateAsync(movie.Id, new MovieMetadata(newTitle, null, null, 100, null, null, genres.Select(g => g.Id).ToList()), default);

        await using var verifyDb = fixture.CreateDbContext();
        var document = await verifyDb.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Equal(original.Id, document.Id);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.Equal(0, document.AttemptCount);
        Assert.Contains(newTitle, document.DocumentText);
        Assert.NotEqual(original.SourceHash, document.SourceHash);
        Assert.Null(document.Vector);
        Assert.Null(document.EmbeddingModelId);
        Assert.Null(document.EmbeddingDimensions);
        Assert.Null(document.IndexedAtUtc);
    }

    [Fact]
    public async Task Assigning_different_genres_changes_the_hash_and_requeues()
    {
        await using var db = fixture.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var genres = await SeedGenresAsync(service);
        var movie = await service.CreateAsync(new MovieMetadata($"Trò chơi teen-{Guid.NewGuid():N}", null, null, 120, null, null, [genres[0].Id]), default);
        var original = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Contains("Drama-", original.DocumentText);

        await service.AssignGenresAsync(movie.Id, [genres[1].Id], default);

        await using var verifyDb = fixture.CreateDbContext();
        var document = await verifyDb.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.DoesNotContain("Drama-", document.DocumentText);
        Assert.Contains("Hành động-", document.DocumentText);
        Assert.NotEqual(original.SourceHash, document.SourceHash);
        Assert.Equal(0, document.AttemptCount);
    }

    [Fact]
    public async Task Renaming_a_genre_requeues_the_documents_of_movies_holding_it()
    {
        await using var db = fixture.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var genres = await SeedGenresAsync(service);
        var movie = await service.CreateAsync(new MovieMetadata($"Hành trình đến phía trước-{Guid.NewGuid():N}", null, null, 95, null, null, [genres[0].Id]), default);
        var original = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);

        var newName = $"Chính kịch-{Guid.NewGuid():N}";
        await service.UpdateGenreAsync(genres[0].Id, newName, $"chinh-kich-{Guid.NewGuid():N}", default);

        await using var verifyDb = fixture.CreateDbContext();
        var document = await verifyDb.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Contains(newName, document.DocumentText);
        Assert.NotEqual(original.SourceHash, document.SourceHash);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
    }

    [Fact]
    public async Task Noop_metadata_update_keeps_an_indexed_row_indexed_with_unchanged_hash()
    {
        await using var db = fixture.CreateDbContext();
        var service = DatabaseTestData.CreateCatalogService(db);
        var genres = await SeedGenresAsync(service);
        var title = $"Kinh nghiệm vàng-{Guid.NewGuid():N}";
        var genreIds = genres.Select(g => g.Id).ToList();
        var movie = await service.CreateAsync(new MovieMetadata(title, "Golden experience", "Một bộ phim thực tế.", 88, new DateOnly(2026, 1, 30), "T16", genreIds), default);
        await using (var indexDb = fixture.CreateDbContext())
        {
            var document = await indexDb.MovieSearchDocuments.SingleAsync(d => d.MovieId == movie.Id);
            document.MarkIndexed("gemini-embedding-2", 768, [0.1f, 0.2f], DateTime.UtcNow);
            await indexDb.SaveChangesAsync();
        }

        await service.UpdateAsync(movie.Id, new MovieMetadata(title, "Golden experience", "Một bộ phim thực tế.", 88, new DateOnly(2026, 1, 30), "T16", genreIds), default);

        await using var verifyDb = fixture.CreateDbContext();
        var stored = await verifyDb.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Equal(MovieSearchDocumentStatus.Indexed, stored.Status);
        Assert.Equal("gemini-embedding-2", stored.EmbeddingModelId);
        Assert.NotNull(stored.Vector);
    }

    private static async Task<List<(Guid Id, string Name)>> SeedGenresAsync(IMovieCatalogService service)
    {
        var drama = await service.CreateGenreAsync($"Drama-{Guid.NewGuid():N}", $"drama-{Guid.NewGuid():N}", default);
        var action = await service.CreateGenreAsync($"Hành động-{Guid.NewGuid():N}", $"hanh-dong-{Guid.NewGuid():N}", default);
        return [(drama.Id, drama.Name), (action.Id, action.Name)];
    }
}
