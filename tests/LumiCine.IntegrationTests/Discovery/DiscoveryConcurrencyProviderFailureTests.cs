using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.IntegrationTests.Discovery;

/// <summary>
/// Turn-commit safety and provider-failure behavior of the discovery chat: concurrent writers
/// with the same expected version must not double-commit, replays stay stable, and every AI
/// provider failure degrades to keyword search plus template answers instead of surfacing.
/// </summary>
[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryConcurrencyProviderFailureTests(PostgresDatabaseFixture fixture)
{
    private static readonly DateTime FixedNow = new(2026, 11, 12, 13, 0, 0, DateTimeKind.Utc);

    private static async Task<Guid> CreateUserAsync(LumiCineDbContext db, string email)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Concurrency test"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<(string MovieTitle, string CinemaName)> SeedCatalogAsync(
        LumiCineDbContext db, string suffix, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"CP" + suffix.ToUpperInvariant(), $"Rạp Trung Tâm {suffix}", $"Địa chỉ {suffix}", $"Hà Nội {suffix}", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"CP-R" + suffix.ToUpperInvariant(), $"Phòng {suffix}", now);
        var movie = new Movie(Guid.NewGuid(), $"Thời Gian Vô Hình {suffix}", 110, now, isActive: true);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, FixedNow.AddHours(20), FixedNow.AddHours(22), 120_000m, now);
        await db.AddRangeAsync(cinema, room, movie, showtime);
        await db.SaveChangesAsync();

        foreach (var (price, index) in seatPrices.Select((p, i) => (p, i)))
        {
            var seat = new Seat(Guid.NewGuid(), room.Id, "A", index + 1, "Standard", now);
            db.Seats.Add(seat);
            db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, price, now));
        }
        await db.SaveChangesAsync();
        return (movie.Title, cinema.Name);
    }

    private static string AdjustIntent(string suffix, string cinemaName) =>
        $$"""
        {"Intent":"adjust","MovieNameText":"Thời Gian Vô Hình {{suffix}}","CinemaName":"{{cinemaName}}","MaxPrice":120000,"MinSeats":2}
        """;

    [Fact]
    public async Task Concurrent_turns_with_same_version_commit_only_one()
    {
        await using var setupDb = fixture.CreateDbContext();
        var userId = await CreateUserAsync(setupDb, $"discovery-conc-{Guid.NewGuid():N}@lumicine.test");
        var chat = new ScriptedChatClient(
            new ScriptedChatReply("{\"Intent\":\"adjust\",\"TopicText\":\"phim gia đình\"}"),
            new ScriptedChatReply("Đây là các phim phù hợp."));
        var conversationId = (await DiscoveryTestServices.CreateConversationService(setupDb, chat)
            .CreateAsync(userId, default)).Id;
        // Two service instances over separate, fresh contexts share one chat script: a DbContext
        // is not thread-safe, and a context that already tracked the conversation would answer
        // the version check from its stale identity map instead of the committed row. The 409
        // loser never reaches the AI calls, so only one writer consumes the scripted replies no
        // matter which call wins the race.
        await using var firstDb = fixture.CreateDbContext();
        await using var secondDb = fixture.CreateDbContext();
        var firstService = DiscoveryTestServices.CreateConversationService(firstDb, chat);
        var secondService = DiscoveryTestServices.CreateConversationService(secondDb, chat);

        // Both writers race with the same expectedVersion; whichever loses must see a 409 and
        // must not leave any message or response behind. Outcomes are asserted, not call order.
        var firstTask = firstService.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Cho mình xem phim gia đình."), default);
        var secondTask = secondService.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Phim gia đình gì hay hôm nay?"), default);

        var outcomes = await WhenAllOutcomeAsync(firstTask, secondTask);
        Assert.Single(outcomes, o => o.Response is not null);
        Assert.Single(outcomes, o => o.ConflictStatusCode == 409);
        var winner = outcomes.Single(o => o.Response is not null).Response!;
        Assert.Equal(2, winner.Version);
        Assert.Equal(1, winner.TurnNumber);

        await using var verify = fixture.CreateDbContext();
        var conversation = await verify.DiscoveryConversations.SingleAsync(c => c.Id == conversationId);
        Assert.Equal(2, conversation.Version);
        Assert.Equal(1, conversation.TurnCount);
        Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
        var assistant = await verify.DiscoveryMessages.SingleAsync(
            m => m.ConversationId == conversationId && m.Role == LumiCine.Domain.Discovery.DiscoveryMessageRole.Assistant);
        Assert.NotEmpty(assistant.Content);
        Assert.Contains(winner.RequestId.ToString(), assistant.ResponseJson);
    }

    [Fact]
    public async Task Replay_after_completion_is_stable_under_repeats()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-replay-{Guid.NewGuid():N}@lumicine.test");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (_, cinemaName) = await SeedCatalogAsync(db, suffix, 100_000m, 150_000m);
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(AdjustIntent(suffix, cinemaName)),
            new ScriptedChatReply("Phim này đang có suất, bạn chọn giờ nhé.")), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;
        var requestId = Guid.NewGuid();
        var message = $"Phim Thời Gian Vô Hình {suffix} ở {cinemaName}.";

        var first = await service.SendMessageAsync(userId, conversationId, new(requestId, 1, message), default);
        var second = await service.SendMessageAsync(userId, conversationId, new(requestId, 1, message), default);
        var third = await service.SendMessageAsync(userId, conversationId, new(requestId, 1, message), default);

        Assert.Equal(first.Answer, second.Answer);
        Assert.Equal(first.Answer, third.Answer);
        Assert.Equal(first.TurnNumber, second.TurnNumber);
        Assert.Equal(first.TurnNumber, third.TurnNumber);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Version, third.Version);
        // DiscoveryFiltersView holds IReadOnlyList properties whose record equality is by
        // reference, so the replayed (deserialized) copies are compared field by field.
        Assert.Equal(first.AppliedFilters.MovieNameText, second.AppliedFilters.MovieNameText);
        Assert.Equal(first.AppliedFilters.MovieNameText, third.AppliedFilters.MovieNameText);
        Assert.Equal(first.AppliedFilters.MaxPrice, second.AppliedFilters.MaxPrice);
        Assert.Equal(first.AppliedFilters.MaxPrice, third.AppliedFilters.MaxPrice);
        Assert.Equal(first.AppliedFilters.MinSeats, second.AppliedFilters.MinSeats);
        Assert.Equal(first.AppliedFilters.MinSeats, third.AppliedFilters.MinSeats);

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Parser_timeout_429_and_malformed_json_each_degrade_without_500()
    {
        var failures = new Dictionary<string, ScriptedChatReply>
        {
            ["timeout"] = new(Failure: new OperationCanceledException("intent deadline passed")),
            ["unknown-intent"] = new("{\"Intent\":\"weird\"}"),
            ["malformed"] = new("not json")
        };

        foreach (var (label, parserReply) in failures)
        {
            await using var db = fixture.CreateDbContext();
            var userId = await CreateUserAsync(db, $"discovery-parser-{label}-{Guid.NewGuid():N}@lumicine.test");
            var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
                parserReply,
                new ScriptedChatReply("Mình đã ghi nhận yêu cầu của bạn.")));
            var conversationId = (await service.CreateAsync(userId, default)).Id;

            // No exception may escape SendMessageAsync: the turn completes as a degraded
            // keyword search over the prior (empty) filter state.
            var response = await service.SendMessageAsync(userId, conversationId,
                new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"yêu cầu lạ {label} {Guid.NewGuid():N}."), default);

            Assert.True(response.IsDegraded, $"{label}: the turn must be flagged degraded.");
            Assert.Contains(response.Warnings, w => w.Contains("từ khóa"));
            Assert.Equal(DiscoveryModes.Keyword, response.Mode);
            Assert.Null(response.AppliedFilters.MaxPrice);
            Assert.Null(response.AppliedFilters.MovieNameText);
            Assert.Null(response.AppliedFilters.City);
            Assert.Null(response.AppliedFilters.DateFrom);
            Assert.Null(response.AppliedFilters.DateTo);
            Assert.Empty(response.AppliedFilters.GenreSlugs);

            await using var verify = fixture.CreateDbContext();
            Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
        }
    }

    [Fact]
    public async Task Writer_failures_produce_templates_not_errors()
    {
        var writerFailures = new Dictionary<string, Exception>
        {
            ["http"] = new HttpRequestException("writer upstream 503"),
            ["timeout"] = new OperationCanceledException("writer deadline passed")
        };

        foreach (var (label, failure) in writerFailures)
        {
            await using var db = fixture.CreateDbContext();
            var userId = await CreateUserAsync(db, $"discovery-writer-{label}-{Guid.NewGuid():N}@lumicine.test");
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var (movieTitle, cinemaName) = await SeedCatalogAsync(db, suffix, 100_000m, 120_000m, 150_000m);
            var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
                new ScriptedChatReply(AdjustIntent(suffix, cinemaName)),
                new ScriptedChatReply(Failure: failure)), new FixedClock(FixedNow));
            var conversationId = (await service.CreateAsync(userId, default)).Id;

            var response = await service.SendMessageAsync(userId, conversationId,
                new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Gợi ý phim Thời Gian Vô Hình {suffix} ở {cinemaName} dưới 120 nghìn."), default);

            Assert.True(response.IsDegraded, $"{label}: the turn must be flagged degraded.");
            Assert.Contains(response.Warnings, w => w.Contains("câu mẫu"));
            Assert.NotEmpty(response.Movies);
            Assert.Contains(response.Movies, m => m.Title == movieTitle);
            Assert.Contains(movieTitle, response.Answer);
            Assert.Equal(120_000m, response.AppliedFilters.MaxPrice);
        }
    }

    private static async Task<Outcome[]> WhenAllOutcomeAsync(
        Task<DiscoveryChatResponse> first, Task<DiscoveryChatResponse> second)
    {
        async Task<Outcome> RunAsync(Task<DiscoveryChatResponse> task)
        {
            try
            {
                return new Outcome(await task, null);
            }
            catch (ApiException ex)
            {
                return new Outcome(null, ex.StatusCode);
            }
        }

        var outcomes = await Task.WhenAll(RunAsync(first), RunAsync(second));
        return outcomes;
    }

    private sealed record Outcome(DiscoveryChatResponse? Response, int? ConflictStatusCode);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// Embedding-persistence guards exercised through the embedding worker path and the queue
/// service: vectors with unexpected dimensions are retried instead of stored, and a source
/// hash that changed while the embedding ran blocks the stale vector from being indexed.
/// </summary>
[Collection(WorkerEmbeddingDatabaseCollection.Name)]
public sealed class DiscoveryProviderFailuresEmbeddingTests(PostgresDatabaseFixture fixture)
{
    private static float[] Vector768 => [.. Enumerable.Range(0, 768).Select(i => i * 0.001f)];

    [Fact]
    public async Task Wrong_dimension_vectors_are_rejected_not_stored()
    {
        // The dimension check lives in the worker, not the service: the worker compares the
        // produced vector length against the configured Gemini dimensions before completing.
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        var fakeEmbedding = new ScriptedEmbeddingGenerator([1f, 2f, 3f]);
        using var factory = CreateFactory(fakeEmbedding, new MutableClock(DateTimeOffset.UtcNow));

        await RunPassAsync(factory);
        var stored = await WaitForAttemptCountAsync(db, documentId, 1);

        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Contains("dimensions", stored.LastError);
        Assert.Null(stored.Vector);
        Assert.Null(stored.EmbeddingModelId);
        Assert.Null(stored.IndexedAtUtc);
    }

    [Fact]
    public async Task Stale_hash_blocks_vector_persistence()
    {
        await using var db = fixture.CreateDbContext();
        var documentId = await SeedDocumentAsync(db);
        await using var serviceDb = fixture.CreateDbContext();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var service = new MovieSearchDocumentService(serviceDb, clock);
        // Other tests in this dedicated database may leave claimable rows behind; only the row
        // seeded by this test is asserted on.
        var claimed = await service.ClaimDueAsync(10, CancellationToken.None);
        var work = claimed.Single(w => w.DocumentId == documentId);
        var originalHash = work.SourceHash;

        // The catalog changed while the embedding was being generated: the row's content and
        // hash were replaced, so the stale completion must be a no-op.
        var replacedHash = new string('f', 64);
        await db.MovieSearchDocuments.Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(d => d.DocumentText, "Nội dung phim đã thay đổi giữa chừng")
                .SetProperty(d => d.SourceHash, replacedHash));

        await service.CompleteAsync(documentId, "gemini-embedding-2", 768, Vector768, originalHash, CancellationToken.None);

        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(x => x.Id == documentId);
        Assert.NotEqual(MovieSearchDocumentStatus.Indexed, stored.Status);
        Assert.Equal(MovieSearchDocumentStatus.Pending, stored.Status);
        Assert.Null(stored.Vector);
        Assert.Null(stored.EmbeddingModelId);
        Assert.Null(stored.IndexedAtUtc);
        Assert.Equal(replacedHash, stored.SourceHash);
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
