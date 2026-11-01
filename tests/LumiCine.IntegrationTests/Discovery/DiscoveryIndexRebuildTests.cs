using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
public sealed class DiscoveryIndexRebuildTests(PostgresDatabaseFixture fixture)
{
    private static MovieSearchDocumentBuilder CreateBuilder() =>
        new(Options.Create(new GeminiOptions()));

    [Fact]
    public async Task Rebuild_endpoint_queues_stale_and_missing_documents_and_is_idempotent_afterwards()
    {
        // Bring the shared catalog's queue to a steady state so the rebuild result counts only
        // the rows this test seeds.
        var builder = CreateBuilder();
        await using (var db = fixture.CreateDbContext())
        {
            var flush = CreateService(db);
            await flush.RebuildAsync(default);
        }

        var now = DateTime.UtcNow;
        var staleMovieTitle = $"Người khách vô danh-{Guid.NewGuid():N}";
        var staleMovieNewTitle = $"Người khách vô danh (bản mới)-{Guid.NewGuid():N}";
        var missingMovieTitle = $"Phim chưa vào hàng đợi-{Guid.NewGuid():N}";
        var upToDateMovieTitle = $"Phim đã đồng bộ-{Guid.NewGuid():N}";
        Guid staleMovieId;
        Guid missingMovieId;
        Guid upToDateMovieId;
        await using (var db = fixture.CreateDbContext())
        {
            var staleMovie = new Movie(Guid.NewGuid(), staleMovieTitle, 90, now);
            var missingMovie = new Movie(Guid.NewGuid(), missingMovieTitle, 95, now);
            var upToDateMovie = new Movie(Guid.NewGuid(), upToDateMovieTitle, 100, now);
            db.Movies.AddRange(staleMovie, missingMovie, upToDateMovie);
            await db.SaveChangesAsync();

            // The stale row was indexed against the original title; the catalog source is then
            // rewritten directly in the database without going through the invalidating upsert.
            var originalText = builder.BuildDocumentText(staleMovieTitle, null, null, []);
            var staleDocument = new MovieSearchDocument(Guid.NewGuid(), staleMovie.Id, originalText, builder.ComputeSourceHash(originalText), now);
            staleDocument.MarkIndexed("gemini-embedding-2", 768, [0.1f, 0.2f], now);
            db.Entry(staleMovie).Property(m => m.Title).CurrentValue = staleMovieNewTitle;

            var upToDateText = builder.BuildDocumentText(upToDateMovieTitle, null, null, []);
            var upToDateDocument = new MovieSearchDocument(Guid.NewGuid(), upToDateMovie.Id, upToDateText, builder.ComputeSourceHash(upToDateText), now);
            upToDateDocument.MarkIndexed("gemini-embedding-2", 768, [0.3f, 0.4f], now);
            db.MovieSearchDocuments.AddRange(staleDocument, upToDateDocument);
            await db.SaveChangesAsync();

            staleMovieId = staleMovie.Id;
            missingMovieId = missingMovie.Id;
            upToDateMovieId = upToDateMovie.Id;
        }

        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);

        using (var firstResponse = await client.PostAsync("/api/admin/discovery/index/rebuild", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            using var body = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
            Assert.Equal(2, body.RootElement.GetProperty("queuedCount").GetInt32());
            Assert.True(body.RootElement.TryGetProperty("queuedAtUtc", out _));
            Assert.True(body.RootElement.GetProperty("queuedAtUtc").GetDateTime() <= DateTime.UtcNow.AddSeconds(5));
        }

        await using (var verifyDb = fixture.CreateDbContext())
        {
            var documents = await verifyDb.MovieSearchDocuments.AsNoTracking()
                .ToDictionaryAsync(d => d.MovieId);

            var upToDateDocument = documents[upToDateMovieId];
            Assert.Equal(MovieSearchDocumentStatus.Indexed, upToDateDocument.Status);
            Assert.Equal("gemini-embedding-2", upToDateDocument.EmbeddingModelId);
            Assert.NotNull(upToDateDocument.Vector);
            Assert.Equal(builder.ComputeSourceHash(builder.BuildDocumentText(upToDateMovieTitle, null, null, [])), upToDateDocument.SourceHash);

            var staleDocument = documents[staleMovieId];
            Assert.Equal(MovieSearchDocumentStatus.Pending, staleDocument.Status);
            Assert.Equal(builder.ComputeSourceHash(builder.BuildDocumentText(staleMovieNewTitle, null, null, [])), staleDocument.SourceHash);
            Assert.Contains(staleMovieNewTitle, staleDocument.DocumentText);

            var createdDocument = documents[missingMovieId];
            Assert.Equal(MovieSearchDocumentStatus.Pending, createdDocument.Status);
            Assert.Equal(builder.ComputeSourceHash(builder.BuildDocumentText(missingMovieTitle, null, null, [])), createdDocument.SourceHash);
            Assert.Equal(builder.BuildDocumentText(missingMovieTitle, null, null, []), createdDocument.DocumentText);
        }

        using (var secondResponse = await client.PostAsync("/api/admin/discovery/index/rebuild", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            using var body = await JsonDocument.ParseAsync(await secondResponse.Content.ReadAsStreamAsync());
            Assert.Equal(0, body.RootElement.GetProperty("queuedCount").GetInt32());
        }
    }

    [Fact]
    public async Task Rebuild_endpoint_requires_admin()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = factory.CreateClient();
        var staff = await CreateUserAsync(factory, RoleNames.Staff);

        using (var anonymous = await client.PostAsync("/api/admin/discovery/index/rebuild", content: null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal("application/problem+json", anonymous.Content.Headers.ContentType?.MediaType);
            using var problem = await JsonDocument.ParseAsync(await anonymous.Content.ReadAsStreamAsync());
            Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff);
        using (var staffResponse = await client.PostAsync("/api/admin/discovery/index/rebuild", content: null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffResponse.StatusCode);
            Assert.Equal("application/problem+json", staffResponse.Content.Headers.ContentType?.MediaType);
            using var problem = await JsonDocument.ParseAsync(await staffResponse.Content.ReadAsStreamAsync());
            Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
        }
    }

    private static DiscoveryIndexAdminService CreateService(LumiCineDbContext db) =>
        new(db, TimeProvider.System, CreateBuilder(), Options.Create(new GeminiOptions()));

    private static async Task<string> CreateUserAsync(ApiWebApplicationFactory factory, string role)
    {
        var email = $"discovery-rebuild-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"Discovery rebuild {role}",
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
}
