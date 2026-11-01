using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryIndexStatusTests(PostgresDatabaseFixture fixture)
{
    private static MovieSearchDocumentBuilder CreateBuilder() =>
        new(Options.Create(new GeminiOptions()));

    private static DiscoveryIndexAdminService CreateService(LumiCineDbContext db) =>
        new(db, TimeProvider.System, CreateBuilder(), Options.Create(new GeminiOptions()));

    [Fact]
    public async Task GetStatus_counts_mixed_document_states_across_the_catalog()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var before = await service.GetStatusAsync(default);

        await SeedMixedStatesAsync(db);
        var after = await service.GetStatusAsync(default);

        Assert.Equal(before.IndexedCount + 3, after.IndexedCount); // current indexed + stale indexed + inactive stale indexed
        Assert.Equal(before.PendingCount + 1, after.PendingCount); // current pending
        Assert.Equal(before.FailedCount + 1, after.FailedCount); // exhausted failed
        Assert.Equal(before.TotalMovies + 6, after.TotalMovies); // five active + one inactive
        Assert.True(after.GeneratedAtUtc <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Rebuild_queues_stale_missing_and_failed_documents_and_leaves_current_ones()
    {
        await using var db = fixture.CreateDbContext();
        var service = CreateService(db);
        var seed = await SeedMixedStatesAsync(db);
        var before = await service.GetStatusAsync(default);

        var result = await service.RebuildAsync(default);
        var after = await service.GetStatusAsync(default);

        Assert.Equal(after.PendingCount - before.PendingCount, result.QueuedCount);
        Assert.True(result.QueuedCount >= 3);
        Assert.True(Math.Abs((result.QueuedAtUtc - DateTime.UtcNow).TotalSeconds) < 30);
        Assert.Equal(before.TotalMovies, after.TotalMovies);

        await using var verifyDb = fixture.CreateDbContext();
        var builder = CreateBuilder();
        var documents = await verifyDb.MovieSearchDocuments.AsNoTracking().ToDictionaryAsync(d => d.MovieId);

        Assert.True(documents.TryGetValue(seed.CurrentIndexed.Id, out var currentIndexedDocument));
        Assert.Equal(MovieSearchDocumentStatus.Indexed, currentIndexedDocument.Status);
        Assert.Equal("gemini-embedding-2", currentIndexedDocument.EmbeddingModelId);
        Assert.NotNull(currentIndexedDocument.Vector);

        var staleDocument = documents[seed.StaleIndexed.Id];
        Assert.Equal(MovieSearchDocumentStatus.Pending, staleDocument.Status);
        Assert.Equal(builder.ComputeSourceHash(builder.BuildDocumentText(seed.StaleIndexed.Title, null, null, [])), staleDocument.SourceHash);
        Assert.Contains(seed.StaleIndexed.Title, staleDocument.DocumentText);

        Assert.Equal(MovieSearchDocumentStatus.Pending, documents[seed.CurrentPending.Id].Status);

        var failedDocument = documents[seed.Failed.Id];
        Assert.Equal(MovieSearchDocumentStatus.Pending, failedDocument.Status);
        Assert.Equal(0, failedDocument.AttemptCount);
        Assert.Null(failedDocument.LastError);

        Assert.True(documents.TryGetValue(seed.Missing.Id, out var createdDocument));
        Assert.Equal(MovieSearchDocumentStatus.Pending, createdDocument.Status);
        Assert.Equal(builder.ComputeSourceHash(builder.BuildDocumentText(seed.Missing.Title, null, null, [])), createdDocument.SourceHash);

        var inactiveDocument = documents[seed.InactiveStale.Id];
        Assert.Equal(MovieSearchDocumentStatus.Indexed, inactiveDocument.Status);
        Assert.NotEqual(builder.ComputeSourceHash(builder.BuildDocumentText(seed.InactiveStale.Title, null, null, [])), inactiveDocument.SourceHash);
    }

    [Fact]
    public async Task Index_status_endpoint_requires_admin_and_reports_the_queue_snapshot()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = factory.CreateClient();
        var staff = await CreateUserAsync(factory, RoleNames.Staff);
        var admin = await CreateUserAsync(factory, RoleNames.Admin);

        using (var anonymous = await client.GetAsync("/api/admin/discovery/index"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType?.MediaType);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff);
        using (var staffResponse = await client.GetAsync("/api/admin/discovery/index"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffResponse.StatusCode);
            Assert.Equal("application/problem+json", staffResponse.Content.Headers.ContentType?.MediaType);
            using var problem = await JsonDocument.ParseAsync(await staffResponse.Content.ReadAsStreamAsync());
            Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        int indexedBefore;
        int totalMoviesBefore;
        using (var first = await client.GetAsync("/api/admin/discovery/index"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            using var body = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
            Assert.All(new[] { "indexedCount", "pendingCount", "failedCount", "totalMovies", "generatedAtUtc" },
                field => Assert.True(body.RootElement.TryGetProperty(field, out _), $"Missing {field}."));
            indexedBefore = body.RootElement.GetProperty("indexedCount").GetInt32();
            totalMoviesBefore = body.RootElement.GetProperty("totalMovies").GetInt32();
        }

        // Seed one indexed movie, then confirm the snapshot reflects it through the JSON mapping.
        var movieTitle = $"Snapshot-{Guid.NewGuid():N}";
        var indexedAtUtc = DateTime.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            var movie = new Movie(Guid.NewGuid(), movieTitle, 90, indexedAtUtc);
            db.Movies.Add(movie);
            var builder = CreateBuilder();
            var text = builder.BuildDocumentText(movieTitle, null, null, []);
            var document = new MovieSearchDocument(Guid.NewGuid(), movie.Id, text, builder.ComputeSourceHash(text), indexedAtUtc);
            document.MarkIndexed("gemini-embedding-2", 768, [0.1f, 0.2f], indexedAtUtc);
            db.MovieSearchDocuments.Add(document);
            await db.SaveChangesAsync();
        }
        using (var second = await client.GetAsync("/api/admin/discovery/index"))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            using var body = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
            Assert.Equal(indexedBefore + 1, body.RootElement.GetProperty("indexedCount").GetInt32());
            Assert.Equal(totalMoviesBefore + 1, body.RootElement.GetProperty("totalMovies").GetInt32());
            Assert.True(body.RootElement.GetProperty("generatedAtUtc").GetDateTime() <= DateTime.UtcNow.AddSeconds(5));
        }
    }

    private static async Task<MixedIndexSeed> SeedMixedStatesAsync(LumiCineDbContext db)
    {
        var now = DateTime.UtcNow;
        var builder = CreateBuilder();
        var currentIndexed = new Movie(Guid.NewGuid(), $"Chuyến tàu ký ức-{Guid.NewGuid():N}", 90, now);
        var staleIndexed = new Movie(Guid.NewGuid(), $"Ngôi sao dẫn lối-{Guid.NewGuid():N}", 100, now);
        var currentPending = new Movie(Guid.NewGuid(), $"Kẻ trộm bóng-{Guid.NewGuid():N}", 110, now);
        var failed = new Movie(Guid.NewGuid(), $"Mùa hè sắc đỏ-{Guid.NewGuid():N}", 120, now);
        var missing = new Movie(Guid.NewGuid(), $"Thành phố không ngủ-{Guid.NewGuid():N}", 130, now);
        var inactiveStale = new Movie(Guid.NewGuid(), $"Bản hòa ca cuối-{Guid.NewGuid():N}", 140, now, isActive: false);
        db.Movies.AddRange(currentIndexed, staleIndexed, currentPending, failed, missing, inactiveStale);
        await db.SaveChangesAsync();

        var currentIndexedText = builder.BuildDocumentText(currentIndexed.Title, null, null, []);
        var currentIndexedDocument = new MovieSearchDocument(Guid.NewGuid(), currentIndexed.Id, currentIndexedText, builder.ComputeSourceHash(currentIndexedText), now);
        currentIndexedDocument.MarkIndexed("gemini-embedding-2", 768, [0.1f, 0.2f], now);

        var staleText = builder.BuildDocumentText(staleIndexed.Title + " (bản cũ)", null, null, []);
        var staleIndexedDocument = new MovieSearchDocument(Guid.NewGuid(), staleIndexed.Id, staleText, builder.ComputeSourceHash(staleText), now);
        staleIndexedDocument.MarkIndexed("gemini-embedding-2", 768, [0.3f, 0.4f], now);

        var currentPendingText = builder.BuildDocumentText(currentPending.Title, null, null, []);
        var currentPendingDocument = new MovieSearchDocument(Guid.NewGuid(), currentPending.Id, currentPendingText, builder.ComputeSourceHash(currentPendingText), now);

        var failedText = builder.BuildDocumentText(failed.Title, null, null, []);
        var failedDocument = new MovieSearchDocument(Guid.NewGuid(), failed.Id, failedText, builder.ComputeSourceHash(failedText), now);
        for (var attempt = 0; attempt < MovieSearchDocument.MaxAttempts; attempt++)
            failedDocument.ScheduleRetry(now, "embedding provider unavailable");

        var inactiveText = builder.BuildDocumentText(inactiveStale.Title + " (bản cũ)", null, null, []);
        var inactiveDocument = new MovieSearchDocument(Guid.NewGuid(), inactiveStale.Id, inactiveText, builder.ComputeSourceHash(inactiveText), now);
        inactiveDocument.MarkIndexed("gemini-embedding-2", 768, [0.5f, 0.6f], now);

        db.MovieSearchDocuments.AddRange(currentIndexedDocument, staleIndexedDocument, currentPendingDocument, failedDocument, inactiveDocument);
        await db.SaveChangesAsync();
        Assert.Equal(MovieSearchDocumentStatus.Failed, failedDocument.Status);
        return new MixedIndexSeed(currentIndexed, staleIndexed, currentPending, failed, missing, inactiveStale);
    }

    private static async Task<string> CreateUserAsync(ApiWebApplicationFactory factory, string role)
    {
        var email = $"discovery-index-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"Discovery index {role}",
            CreatedAtUtc = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        var db = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = user.Id, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) });
        await db.SaveChangesAsync();
        return factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, sessionId, [role]).Value;
    }

    private sealed record MixedIndexSeed(
        Movie CurrentIndexed,
        Movie StaleIndexed,
        Movie CurrentPending,
        Movie Failed,
        Movie Missing,
        Movie InactiveStale);
}
