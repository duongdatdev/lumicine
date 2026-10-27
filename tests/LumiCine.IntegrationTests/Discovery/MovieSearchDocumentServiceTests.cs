using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class MovieSearchDocumentServiceTests(PostgresDatabaseFixture fixture)
{
    private static readonly float[] Vector = [0.25f, 0.5f, 0.75f];

    [Fact]
    public void Builder_composes_title_original_title_genres_and_synopsis()
    {
        var builder = CreateBuilder(new GeminiOptions());

        var text = builder.BuildDocumentText("Dáng đúng", "Correct Posture", "Một phim hài nhẹ nhàng.", ["Hài", "Gia đình"]);

        var lines = text.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("Tiêu đề: Dáng đúng", lines[0]);
        Assert.Equal("Tên gốc: Correct Posture", lines[1]);
        Assert.Equal("Thể loại: Hài, Gia đình", lines[2]);
        Assert.Equal("Nội dung: Một phim hài nhẹ nhàng.", lines[3]);
    }

    [Fact]
    public void Builder_omits_absent_parts_and_collapses_whitespace()
    {
        var builder = CreateBuilder(new GeminiOptions());

        var text = builder.BuildDocumentText("Phim\tvui \n nhộn", "  ", "", []);

        Assert.Equal("Tiêu đề: Phim vui nhộn", text);

        var withSynopsis = builder.BuildDocumentText("Phim vui nhộn", null, "  Nhiều   tiếng   cười.\n  ", []);
        Assert.Equal("Tiêu đề: Phim vui nhộn\nNội dung: Nhiều tiếng cười.", withSynopsis);
    }

    [Fact]
    public void Builder_truncates_only_the_synopsis_to_the_character_cap()
    {
        var builder = CreateBuilder(new GeminiOptions { DocumentMaxCharacters = 50 });
        var synopsis = new string('m', 400);

        var text = builder.BuildDocumentText("Dáng đúng", null, synopsis, []);

        Assert.True(text.Length <= 50, $"Expected at most 50 characters but got {text.Length}.");
        Assert.StartsWith("Tiêu đề: Dáng đúng\nNội dung: ", text);
        Assert.EndsWith(new string('m', 21), text);
        Assert.DoesNotContain(synopsis, text);
    }

    [Fact]
    public void Hash_is_uppercase_hex_and_stable_for_the_same_text()
    {
        var builder = CreateBuilder(new GeminiOptions());
        var text = "Tiêu đề: Dáng đúng\nThể loại: Hài";

        var first = builder.ComputeSourceHash(text);
        var second = builder.ComputeSourceHash(text);
        var other = builder.ComputeSourceHash(text + "!");

        Assert.Equal(64, first.Length);
        Assert.True(first.All(char.IsAsciiHexDigit));
        Assert.Equal(first, first.ToUpperInvariant());
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public async Task Claim_due_leases_rows_and_blocks_a_second_claim_before_expiry()
    {
        await using var db = fixture.CreateDbContext();
        await db.MovieSearchDocuments.ExecuteDeleteAsync();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var movie = (await DatabaseTestData.CreateBaseAsync(db)).Movie;
        var document = new MovieSearchDocument(Guid.NewGuid(), movie.Id, "Văn bản gốc", new string('a', 64), clock.GetUtcNow().UtcDateTime);
        db.MovieSearchDocuments.Add(document);
        await db.SaveChangesAsync();
        var service = new MovieSearchDocumentService(db, clock);

        var claimed = await service.ClaimDueAsync(10, CancellationToken.None);
        var work = Assert.Single(claimed);
        Assert.Equal(document.Id, work.DocumentId);
        Assert.Equal(movie.Id, work.MovieId);
        Assert.Equal("Văn bản gốc", work.DocumentText);
        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        var leaseDelay = stored.LeaseUntilUtc!.Value - clock.GetUtcNow().UtcDateTime;
        Assert.InRange(leaseDelay.TotalMinutes, 4.9, 5.1);

        Assert.Empty(await service.ClaimDueAsync(10, CancellationToken.None));

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Single(await service.ClaimDueAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Complete_saves_vector_model_and_dimensions_when_the_hash_matches()
    {
        await using var db = fixture.CreateDbContext();
        await db.MovieSearchDocuments.ExecuteDeleteAsync();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var movie = (await DatabaseTestData.CreateBaseAsync(db)).Movie;
        var hash = new string('b', 64);
        var document = new MovieSearchDocument(Guid.NewGuid(), movie.Id, "Văn bản gốc", hash, clock.GetUtcNow().UtcDateTime);
        db.MovieSearchDocuments.Add(document);
        await db.SaveChangesAsync();
        // The worker claims and completes through its own scope, so the service gets a fresh context.
        await using var serviceDb = fixture.CreateDbContext();
        var service = new MovieSearchDocumentService(serviceDb, clock);
        Assert.Single(await service.ClaimDueAsync(10, CancellationToken.None));

        await service.CompleteAsync(document.Id, "gemini-embedding-2", 3, Vector, hash, CancellationToken.None);

        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        Assert.Equal(MovieSearchDocumentStatus.Indexed, stored.Status);
        Assert.Equal("gemini-embedding-2", stored.EmbeddingModelId);
        Assert.Equal(3, stored.EmbeddingDimensions);
        Assert.Equal(Vector, stored.Vector);
        Assert.NotNull(stored.IndexedAtUtc);
        Assert.Null(stored.LeaseUntilUtc);
        Assert.Null(stored.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Complete_is_a_no_op_when_the_stored_hash_changed_after_claiming()
    {
        await using var db = fixture.CreateDbContext();
        await db.MovieSearchDocuments.ExecuteDeleteAsync();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var movie = (await DatabaseTestData.CreateBaseAsync(db)).Movie;
        var originalHash = new string('c', 64);
        var document = new MovieSearchDocument(Guid.NewGuid(), movie.Id, "Văn bản gốc", originalHash, clock.GetUtcNow().UtcDateTime);
        db.MovieSearchDocuments.Add(document);
        await db.SaveChangesAsync();
        await using var serviceDb = fixture.CreateDbContext();
        var service = new MovieSearchDocumentService(serviceDb, clock);
        Assert.Single(await service.ClaimDueAsync(10, CancellationToken.None));

        // The catalog changed while the embedding was being generated: the row was re-queued separately.
        var newHash = new string('d', 64);
        await db.MovieSearchDocuments.Where(x => x.Id == document.Id)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.DocumentText, "Văn bản mới")
                .SetProperty(x => x.SourceHash, newHash));

        await service.CompleteAsync(document.Id, "gemini-embedding-2", 3, Vector, originalHash, CancellationToken.None);

        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Equal("Văn bản mới", stored.DocumentText);
        Assert.Equal(newHash, stored.SourceHash);
        Assert.Null(stored.Vector);
        Assert.Null(stored.EmbeddingModelId);
        Assert.Null(stored.IndexedAtUtc);
    }

    [Fact]
    public async Task Retry_increments_attempts_and_schedules_the_next_attempt()
    {
        await using var db = fixture.CreateDbContext();
        await db.MovieSearchDocuments.ExecuteDeleteAsync();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var movie = (await DatabaseTestData.CreateBaseAsync(db)).Movie;
        var document = new MovieSearchDocument(Guid.NewGuid(), movie.Id, "Văn bản gốc", new string('a', 64), clock.GetUtcNow().UtcDateTime);
        db.MovieSearchDocuments.Add(document);
        await db.SaveChangesAsync();
        var service = new MovieSearchDocumentService(db, clock);

        await service.RetryAsync(document.Id, "Gemini quota exceeded", CancellationToken.None);

        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal("Gemini quota exceeded", stored.LastError);
        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Null(stored.LeaseUntilUtc);
        var delay = stored.NextAttemptAtUtc!.Value - clock.GetUtcNow().UtcDateTime;
        Assert.InRange(delay.TotalMinutes, 0.9, 1.1);

        await service.RetryAsync(document.Id, "Gemini quota exceeded again", CancellationToken.None);
        var retried = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        Assert.Equal(2, retried.AttemptCount);
        Assert.InRange((retried.NextAttemptAtUtc!.Value - clock.GetUtcNow().UtcDateTime).TotalMinutes, 4.9, 5.1);
    }

    [Fact]
    public async Task Complete_and_retry_ignores_missing_rows()
    {
        await using var db = fixture.CreateDbContext();
        var service = new MovieSearchDocumentService(db, TimeProvider.System);

        await service.CompleteAsync(Guid.NewGuid(), "gemini-embedding-2", 3, Vector, new string('a', 64), CancellationToken.None);
        await service.RetryAsync(Guid.NewGuid(), "missing", CancellationToken.None);
    }

    private static MovieSearchDocumentBuilder CreateBuilder(GeminiOptions options) => new(Options.Create(options));

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public void Advance(TimeSpan delta) => Now = Now.Add(delta);

        private DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
