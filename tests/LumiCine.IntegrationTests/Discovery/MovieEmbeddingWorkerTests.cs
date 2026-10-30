using LumiCine.Application.Catalog;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(WorkerEmbeddingDatabaseCollection.Name)]
public sealed class MovieEmbeddingWorkerTests(PostgresDatabaseFixture fixture)
{
    private static float[] Vector768 => [.. Enumerable.Range(0, 768).Select(i => i * 0.001f)];

    [Fact]
    public async Task Worker_indexes_a_pending_document_with_the_fake_vector()
    {
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        var fakeEmbedding = new ScriptedEmbeddingGenerator(Vector768);
        using var factory = CreateFactory(fakeEmbedding, new MutableClock(DateTimeOffset.UtcNow));

        await RunPassAsync(factory);

        var stored = await WaitForStatusAsync(db, documentId, MovieSearchDocumentStatus.Indexed);
        Assert.Equal(Vector768, stored.Vector);
        Assert.Equal("gemini-embedding-2", stored.EmbeddingModelId);
        Assert.Equal(768, stored.EmbeddingDimensions);
        Assert.NotNull(stored.IndexedAtUtc);
        Assert.Null(stored.LeaseUntilUtc);
        Assert.Null(stored.LastError);
        Assert.Contains(stored.DocumentText, Assert.Single(fakeEmbedding.Requests));
    }

    [Fact]
    public async Task Worker_retries_a_failed_embedding_with_backoff()
    {
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        var fakeEmbedding = new ScriptedEmbeddingGenerator(Vector768) { Failure = new HttpRequestException("quota") };
        using var factory = CreateFactory(fakeEmbedding, new MutableClock(DateTimeOffset.UtcNow));

        await RunPassAsync(factory);
        var stored = await WaitForAttemptCountAsync(db, documentId, 1);

        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Contains("quota", stored.LastError);
        Assert.Null(stored.Vector);
        Assert.Null(stored.EmbeddingModelId);
        var delay = stored.NextAttemptAtUtc!.Value - stored.UpdatedAtUtc!.Value;
        Assert.InRange(delay.TotalMinutes, 0.9, 1.1);
    }

    [Fact]
    public async Task Worker_skips_documents_invalidated_after_queueing()
    {
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        // The catalog changed after queueing: the row's content and hash were replaced while queued.
        var newHash = new string('e', 64);
        await db.MovieSearchDocuments.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(d => d.DocumentText, "Nội dung đã thay đổi sau khi xếp hàng")
                .SetProperty(d => d.SourceHash, newHash));
        var fakeEmbedding = new ScriptedEmbeddingGenerator(Vector768);
        using var factory = CreateFactory(fakeEmbedding, new MutableClock(DateTimeOffset.UtcNow));

        await RunPassAsync(factory);

        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Equal(0, stored.AttemptCount);
        Assert.Null(stored.Vector);
        Assert.Equal(newHash, stored.SourceHash);
        Assert.Equal("Nội dung đã thay đổi sau khi xếp hàng", stored.DocumentText);
        Assert.Empty(fakeEmbedding.Requests);
    }

    [Fact]
    public async Task Worker_retries_when_the_vector_has_unexpected_dimensions()
    {
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        var fakeEmbedding = new ScriptedEmbeddingGenerator([1f, 2f, 3f]);
        using var factory = CreateFactory(fakeEmbedding, new MutableClock(DateTimeOffset.UtcNow));

        await RunPassAsync(factory);
        var stored = await WaitForAttemptCountAsync(db, documentId, 1);

        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Contains("dimensions", stored.LastError);
        Assert.Null(stored.Vector);
        var delay = stored.NextAttemptAtUtc!.Value - stored.UpdatedAtUtc!.Value;
        Assert.InRange(delay.TotalMinutes, 0.9, 1.1);
    }

    [Fact]
    public async Task Worker_fails_a_document_after_five_attempts()
    {
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        var fakeEmbedding = new ScriptedEmbeddingGenerator(Vector768) { Failure = new HttpRequestException("quota") };
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        using var factory = CreateFactory(fakeEmbedding, clock);

        await RunPassAsync(factory);
        for (var attempt = 1; attempt <= MovieSearchDocument.MaxAttempts; attempt++)
        {
            await WaitForAttemptCountAsync(db, documentId, attempt);
            if (attempt == MovieSearchDocument.MaxAttempts)
                break;
            // Catch the mutable clock up past the next scheduled attempt, then run one more pass.
            var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
            var delta = stored.NextAttemptAtUtc!.Value.AddSeconds(1) - clock.GetUtcNow();
            if (delta > TimeSpan.Zero) clock.Advance(delta);
            await RunPassAsync(factory);
        }

        var failed = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
        Assert.Equal(MovieSearchDocumentStatus.Failed, failed.Status);
        Assert.Equal(5, failed.AttemptCount);
        Assert.Contains("quota", failed.LastError);
        Assert.Null(failed.NextAttemptAtUtc);
        Assert.Null(failed.LeaseUntilUtc);
        Assert.Null(failed.Vector);
    }

    private static async Task<Guid> SeedDocumentAsync(LumiCineDbContext db)
    {
        var service = DatabaseTestData.CreateCatalogService(db);
        var drama = await service.CreateGenreAsync($"Drama-{Guid.NewGuid():N}", $"drama-{Guid.NewGuid():N}", default);
        var movie = await service.CreateAsync(new MovieMetadata($"Phim-{Guid.NewGuid():N}", null, "Một phim điện ảnh thử nghiệm.", 100, null, null, [drama.Id]), default);
        return await db.MovieSearchDocuments.Where(d => d.MovieId == movie.Id).Select(d => d.Id).SingleAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(ScriptedEmbeddingGenerator fakeEmbedding, MutableClock clock)
    {
        var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<GeminiAiClients>(
                        new GeminiAiClients(new ScriptedChatClient(new ScriptedChatReply("ok")), fakeEmbedding)));
                    services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                });
            });
        // Building the host starts the hosted workers; without it the worker never runs.
        _ = factory.Services;
        return factory;
    }

    private static async Task RunPassAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MovieEmbeddingWorker>()
            .ProcessDueDocumentsAsync(1, CancellationToken.None);
    }

    private static async Task<MovieSearchDocument> WaitForStatusAsync(LumiCineDbContext db, Guid documentId, MovieSearchDocumentStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            var stored = await db.MovieSearchDocuments.AsNoTracking()
                .Where(x => x.Id == documentId).SingleOrDefaultAsync(timeout.Token);
            if (stored is not null && stored.Status == status)
                return stored;
            await Task.Delay(200, timeout.Token);
        }
    }

    private static async Task<MovieSearchDocument> WaitForAttemptCountAsync(LumiCineDbContext db, Guid documentId, int attempt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            var stored = await db.MovieSearchDocuments.AsNoTracking()
                .Where(x => x.Id == documentId).SingleOrDefaultAsync(timeout.Token);
            if (stored is not null && stored.AttemptCount >= attempt)
                return stored;
            await Task.Delay(200, timeout.Token);
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public void Advance(TimeSpan delta) => Now = Now.Add(delta);

        private DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
